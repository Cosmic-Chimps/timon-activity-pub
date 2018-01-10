using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Xml.Linq;
using Kroeg.ActivityStreams;
using Kroeg.Server.Configuration;
using Kroeg.Server.Models;
using Kroeg.EntityStore.Store;
using Kroeg.Server.Tools;
using Newtonsoft.Json;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Kroeg.Server.Salmon;
using Microsoft.Extensions.Configuration;
using Kroeg.Server.Services;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using Kroeg.Server.BackgroundTasks;
using System.IO;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Kroeg.Server.Middleware.Handlers.ClientToServer;
using Kroeg.Server.Middleware.Handlers.Shared;
using System.Data.Common;
using System.Transactions;
using Dapper;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Controllers
{
    [Route("/auth")]
    public class AuthController : Controller
    {
        private readonly DbConnection _connection;
        private readonly UserManager<APUser> _userManager;
        private readonly SignInManager<APUser> _signInManager;
        private readonly JwtTokenSettings _tokenSettings;
        private readonly EntityFlattener _entityFlattener;
        private readonly IEntityStore _entityStore;
        private readonly ServerConfig _entityConfiguration;
        private readonly IDataProtector _dataProtector;
        private readonly IConfigurationRoot _configuration;
        private readonly DeliveryService _deliveryService;
        private readonly SignatureVerifier _verifier;
        private readonly IServiceProvider _provider;
        private readonly CollectionTools _collectionTools;
        private readonly RelevantEntitiesService _relevantEntities;

        public AuthController(DbConnection connection, UserManager<APUser> userManager, SignInManager<APUser> signInManager, JwtTokenSettings tokenSettings, EntityFlattener entityFlattener, IEntityStore entityStore, ServerConfig entityConfiguration, IDataProtectionProvider dataProtectionProvider, IConfigurationRoot configuration, DeliveryService deliveryService, SignatureVerifier verifier, IServiceProvider provider, CollectionTools collectionTools, RelevantEntitiesService relevantEntities)
        {
            _connection = connection;
            _userManager = userManager;
            _signInManager = signInManager;
            _tokenSettings = tokenSettings;
            _entityFlattener = entityFlattener;
            _entityStore = entityStore;
            _entityConfiguration = entityConfiguration;
            _dataProtector = dataProtectionProvider.CreateProtector("OAuth tokens");
            _configuration = configuration;
            _deliveryService = deliveryService;
            _verifier = verifier;
            _provider = provider;
            _collectionTools = collectionTools;
            _relevantEntities = relevantEntities;
        }

        public class LoginViewModel
        {
            public string Username { get; set; }
            public string Password { get; set; }
            public string Redirect { get; set; }
        }

        public class RegisterViewModel
        {
            public string Username { get; set; }
            public string Password { get; set; }
            public string VerifyPassword { get; set; }
            public string Email { get; set; }
            public string Redirect { get; set; }
        }

        public class ChosenActorModel
        {
            public string ActorID { get; set; }
        }

        [HttpGet("login")]
        public IActionResult Login(string returnUrl)
        {
            return View(new LoginViewModel { Redirect = returnUrl });
        }

        [HttpGet("logout")]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        [HttpPost("proxy")]
        public async Task<IActionResult> Proxy(string id)
        {
            if (User.FindFirstValue(JwtTokenSettings.ActorClaim) == null) return Unauthorized();

            APEntity entity = null;
            if (id.StartsWith('@'))
            {
                id = id.Substring(1);
                var spl = id.Split(new char[] { '@' } , 2);
                var host = spl.Length > 1 ? spl[1] : Request.Host.ToString();
                var ent = await _relevantEntities.FindEntitiesWithPreferredUsername(spl[0]);
                var withHost = ent.FirstOrDefault(a => new Uri(a.Id).Host == host);
                if (withHost == null && spl.Length == 1) return NotFound();

                entity = withHost;

                if (entity == null && spl.Length > 1)
                {
                    var hc = new HttpClient();
                    var webfinger = JsonConvert.DeserializeObject<WellKnownController.WebfingerResult>(await hc.GetStringAsync($"https://{host}/.well-known/webfinger?resource=acct:{id}"));
                    var activityStreams = webfinger.links.FirstOrDefault(a => a.rel == "self" && (a.type == "application/activity+json" || a.type == "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\""));
                    if (activityStreams == null) return NotFound();

                    id = activityStreams.href;
                }
            }
            
            if (entity == null) entity = await _entityStore.GetEntity(id, true);

            if (entity == null) return NotFound();

            var unflattened = await _entityFlattener.Unflatten(_entityStore, entity);
            return Content(unflattened.Serialize(true).ToString(), "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"");
        }

        [HttpPost("login"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DoLogin(LoginViewModel model)
        {
            var result = await _signInManager.PasswordSignInAsync(model.Username, model.Password, false, false);

            if (!result.Succeeded) return View("Login", model);
            if (!string.IsNullOrEmpty(model.Redirect)) return RedirectPermanent(model.Redirect);

            return RedirectToActionPermanent("Index", "Settings");
        }

        [HttpGet("register")]
        public IActionResult Register()
        {
            if (!_configuration.GetSection("Kroeg").GetValue<bool>("CanRegister")) return NotFound();
            return View(new RegisterViewModel { });
        }

        [HttpPost("register"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DoRegister(RegisterViewModel model)
        {
            if (!_configuration.GetSection("Kroeg").GetValue<bool>("CanRegister")) return NotFound();
            var apuser = new APUser
            {
                Username = model.Username,
                Email = model.Email
            };

            if ((await _relevantEntities.FindEntitiesWithPreferredUsername(model.Username)).Count > 0)
            {
                ModelState.AddModelError("", "Username is already in use!");
            }

            if (model.Password != model.VerifyPassword)
            {
                ModelState.AddModelError("", "Passwords don't match!");
            }

            if (!ModelState.IsValid) return View("Register", model);

            await _connection.OpenAsync();
            using (var trans = _connection.BeginTransaction())
            {
                var result = await _userManager.CreateAsync(apuser, model.Password);
                if (!result.Succeeded)
                {
                    ModelState.AddModelError("", result.Errors.First().Description);
                }

                if (!ModelState.IsValid) return View("Register", model);

                if (await _connection.ExecuteAsync("select count(*) from \"Users\"") == 1)
                {
//                    await _userManager.AddClaimAsync(apuser, new Claim("admin", "true"));
                }

                await _signInManager.SignInAsync(apuser, false);

                var user = model.Username;

                var obj = new ASObject();
                obj.Type.Add("https://www.w3.org/ns/activitystreams#Person");
                obj["preferredUsername"].Add(ASTerm.MakePrimitive(user));
                obj["name"].Add(ASTerm.MakePrimitive(user));

                var create = new ASObject();
                create.Type.Add("https://www.w3.org/ns/activitystreams#Create");
                create["object"].Add(ASTerm.MakeSubObject(obj));
                create["to"].Add(ASTerm.MakeId("https://www.w3.org/ns/activitystreams#Public"));

                Console.WriteLine($"--- creating actor. Unflattened:\n{create.Serialize().ToString(Formatting.Indented)}");
                var apo = await _entityFlattener.FlattenAndStore(_entityStore, create);
                Console.WriteLine($"Flat: {apo.Data.Serialize().ToString(Formatting.Indented)}\n----");
                var handler = new CreateActorHandler(_entityStore, apo, null, null, User, _collectionTools, _entityConfiguration, _connection);
                handler.UserOverride = apuser.Id;
                await handler.Handle();

                var resultUser = await _entityStore.GetEntity(handler.MainObject.Data["object"].First().Id, false);
                var outbox = await _entityStore.GetEntity(resultUser.Data["outbox"].First().Id, false);
                var delivery = new DeliveryHandler(_entityStore, handler.MainObject, resultUser, outbox, User, _collectionTools, _provider.GetRequiredService<DeliveryService>());
                await delivery.Handle();
                
                trans.Commit();
                return RedirectToActionPermanent("Index", "Settings");
            }

        }

        private string _appendToUri(string uri, string query)
        {
            var builder = new UriBuilder(uri);
            if (builder.Query?.Length > 1)
                builder.Query = builder.Query.Substring(1) + "&" + query;
            else
                builder.Query = query;
            
            return builder.ToString();
        }

        [HttpGet("search"), Authorize]
        public async Task<IActionResult> Search(string type, string data)
        {
            List<JObject> result = new List<JObject>();
            if (type == "emoji")
            {
                result.AddRange((await _relevantEntities.FindEmojiLike(data)).Select(a => a.Data.Serialize(true)));
            }
            else if (type == "actor")
            {
                result.AddRange((await _relevantEntities.FindUsersWithNameLike(data)).Select(a => a.Data.Serialize(true)));
            }

            return Json(result);
        }

        [HttpPost("sharedInbox")]
        public async Task<IActionResult> SharedInbox()
        {
            var userId = await _verifier.Verify(Request.Scheme + "://" + Request.Host.ToUriComponent() + Request.Path, HttpContext);
            if (userId == null) return Unauthorized();
            var reader = new StreamReader(Request.Body);
            var data = ASObject.Parse(await reader.ReadToEndAsync());

            if (!EntityData.IsActivity(data)) return StatusCode(403, "Not an activity?");

            await _connection.OpenAsync();

            using (var transaction = _connection.BeginTransaction())
            {
                APEntity resultEntity;
                if (data["actor"].Any((a) => a.Id == userId))
                {
                    var temporaryStore = new StagingEntityStore(_entityStore);
                    resultEntity = await _entityFlattener.FlattenAndStore(temporaryStore, data, false);
                    await temporaryStore.TrimDown((new Uri(new Uri(userId), "/")).ToString());
                    await temporaryStore.CommitChanges();
                }
                else
                {
                    resultEntity = await _entityStore.GetEntity(data.Id, true);
                    if (resultEntity == null) return StatusCode(202);
                    data = resultEntity.Data;
                }

                var users = await _deliveryService.GetUsersForSharedInbox(data);

                foreach (var user in users)
                {
                    await DeliverToActivityPubTask.Make(new DeliverToActivityPubData { ObjectId = resultEntity.Id, TargetInbox = user.Data["inbox"].First().Id }, _connection);
                }

                transaction.Commit();
                return StatusCode(202);
            }
        }

        private class _jwkKeyset
        {
            [JsonProperty("keys")]
            public List<JObject> Keys { get; set; }
        }

        [HttpGet("jwks")]
        public async Task<IActionResult> GetJsonWebKeys(string id)
        {
            var actor = await _entityStore.GetEntity(id, false);
            if (actor == null || !actor.IsOwner) return NotFound();
            var key = await _verifier.GetJWK(actor);
            var deser = key.Key;
            deser.D = null;

            var jo = JObject.FromObject(deser);
            foreach (var ancestor in jo.Properties().ToList())
            {
                if (ancestor.Name.ToLower() != ancestor.Name) ancestor.Remove();
            }


            return Content(JsonConvert.SerializeObject(new _jwkKeyset { Keys = new List<JObject> { jo } }), "application/json");
        }
    }
}
