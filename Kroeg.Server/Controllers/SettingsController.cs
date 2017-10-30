﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Kroeg.Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Kroeg.ActivityStreams;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Kroeg.Server.Salmon;
using System.IdentityModel.Tokens.Jwt;
using Kroeg.Server.Configuration;
using Microsoft.AspNetCore.Http.Authentication;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Kroeg.Server.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Kroeg.Server.Services;
using Newtonsoft.Json.Linq;
using Kroeg.Server.Middleware.Handlers.ClientToServer;
using Kroeg.Server.Middleware.Handlers.Shared;
using Kroeg.Server.Services.Template;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Data;
using Dapper;
using System.Data.Common;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Controllers
{
    [Route("settings"), Authorize("pass")]
    public class SettingsController : Controller
    {
        public class BaseModel
        {
            public APUser User { get; set; }
            public List<UserActorPermission> Actors { get; set; }
        }

        public class NewActorModel
        {
            public BaseModel Menu { get; set; }

            public string Username { get; set; }
            public string Name { get; set; }
            public string Summary { get; set; }
            public string Type { get; set; }
        }

        public class EditActorModel
        {
            public BaseModel Menu { get; set; }
            public List<UserActorPermission> OtherPeople { get; set; }

            public UserActorPermission Actor { get; set; }
            public string OtherIsAdmin { get; set; }
            public string AuthorizeOther { get; set; }
        }

        private readonly IEntityStore _entityStore;
        private readonly EntityData _entityData;
        private readonly JwtTokenSettings _tokenSettings;
        private readonly SignInManager<APUser> _signInManager;
        private readonly IServiceProvider _provider;
        private readonly IConfigurationRoot _configuration;
        private readonly EntityFlattener _flattener;
        private readonly UserManager<APUser> _userManager;
        private readonly RelevantEntitiesService _relevantEntities;
        private readonly CollectionTools _collectionTools;
        private readonly TemplateService _templateService;
        private readonly DbConnection _connection;

        public SettingsController(DbConnection connection, IEntityStore entityStore, EntityData entityData, JwtTokenSettings tokenSettings, SignInManager<APUser> signInManager, IServiceProvider provider, IConfigurationRoot configuration, EntityFlattener flattener, UserManager<APUser> userManager, RelevantEntitiesService relevantEntities, CollectionTools collectionTools, TemplateService templateService)
        {
            _connection = connection;
            _entityStore = entityStore;
            _entityData = entityData;
            _tokenSettings = tokenSettings;
            _signInManager = signInManager;
            _provider = provider;
            _configuration = configuration;
            _flattener = flattener;
            _userManager = userManager;
            _relevantEntities = relevantEntities;
            _collectionTools = collectionTools;
            _templateService = templateService;
        }

        [AllowAnonymous, HttpGet("templates")]
        public IActionResult Templates()
        {
            Response.Headers.Add("Access-Control-Allow-Origin", "*");
            return Json(_templateService.Templates);
        }

        private async Task<BaseModel> _getUserInfo()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            throw new NotImplementedException();
        }

        [Authorize]
        public async Task<IActionResult> Index()
        {
            var data = await _getUserInfo();
            if (data.Actors.Count == 0) return View("NewActor", new NewActorModel() { Menu = data });
            throw new NotImplementedException();
        }

        [Authorize, HttpGet("new")]
        public async Task<IActionResult> NewActor()
        {
            return View("NewActor", new NewActorModel { Menu = await _getUserInfo() });
        }

        [AllowAnonymous, HttpGet("auth")]
        public async Task<IActionResult> RedeemAuth(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            SecurityToken validatedToken;
            var claims = tokenHandler.ValidateToken(token, _tokenSettings.ValidationParameters, out validatedToken);
            if (claims == null || validatedToken.ValidTo < DateTime.UtcNow) return Unauthorized();

            var userId = claims.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _connection.QuerySingleOrDefaultAsync<APUser>("select * from \"Users\" where \"Id\" = @Id", new { Id = userId });
            await _signInManager.SignInAsync(user, false);
            return RedirectToActionPermanent("Index");
        }

        [Authorize, HttpPost("auth")]
        public IActionResult CreateAuth()
        {
            var claims = new Claim[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, User.FindFirstValue(ClaimTypes.NameIdentifier))
            };

            var jwt = new JwtSecurityToken(
                issuer: _tokenSettings.Issuer,
                audience: _tokenSettings.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.Add(TimeSpan.FromMinutes(5)),
                signingCredentials: _tokenSettings.Credentials
                );

            var encodedJwt = new JwtSecurityTokenHandler().WriteToken(jwt);

            return Ok(Url.Action("RedeemAuth", new { token = encodedJwt }));

        }

        [Authorize, HttpGet("actor")]
        public async Task<IActionResult> Edit(string id)
        {
            var data = await _getUserInfo();
            var ac = await _entityStore.GetEntity(id, false);
            if (ac == null) return await Index();

            var actor = data.Actors.FirstOrDefault(a => a.ActorId == ac.DbId);
            if (actor == null) return await Index();

            throw new NotImplementedException();
        }

        [Authorize, HttpPost("uploadMedia")]
        public async Task<IActionResult> UploadMedia()
        {
            var @object = Request.Form["object"];
            var file = Request.Form.Files["file"];

            var handler = ActivatorUtilities.CreateInstance<GetEntityMiddleware.GetEntityHandler>(_provider, User);
            var obj = ASObject.Parse(@object);
            var mainObj = obj;
            if (obj["object"].Any())
            {
                mainObj = obj["object"].Single().SubObject;
            }

            var uploadPath = _configuration.GetSection("Kroeg")["FileUploadPath"];
            var uploadUri = _configuration.GetSection("Kroeg")["FileUploadUrl"];

            var extension = file.FileName.Split('.').Last().Replace('/', '_');

            var fileName = Guid.NewGuid().ToString() + "." + extension;

            var str = System.IO.File.OpenWrite(uploadPath + fileName);
            await file.CopyToAsync(str);
            str.Dispose();

            mainObj.Replace("url", ASTerm.MakePrimitive(uploadUri + fileName));

            var entity = await _entityStore.GetEntity((string) HttpContext.Items["fullPath"], false);
            try
            {
                var entOut = await handler.Post(HttpContext, (string)HttpContext.Items["fullPath"], entity, obj);
                if (entOut == null)
                    return NotFound();
                obj = await _flattener.Unflatten(_entityStore, entOut);
                return Content(obj.Serialize().ToString(), "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"");
            }
            catch (UnauthorizedAccessException e)
            {
                return StatusCode(403, e);
            }
            catch (InvalidOperationException e)
            {
                return StatusCode(401, e);
            }
       }

        public class RelevantObjectsModel {
            public string id { get; set; }
        }

        [Authorize, HttpPost("relevant")]
        public async Task<IActionResult> RelevantEntities(RelevantObjectsModel model)
        {
            var user = User.FindFirstValue(JwtTokenSettings.ActorClaim);
            if (user == null) return Json(new List<ASObject>());

            var relevant = await _relevantEntities.FindRelevantObject(user, null, model.id);

            ASObject relevantObject = new ASObject();

            foreach (var item in relevant)
            {
                relevantObject["relevant"].Add(ASTerm.MakeSubObject(item.Data));
            }

            return Content((await _flattener.Unflatten(_entityStore, APEntity.From(relevantObject), 5)).Serialize().ToString(), "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"");
        }

        [Authorize, HttpPost("new")]
        public async Task<IActionResult> MakeNewActor(NewActorModel model)
        {
            var data = await _getUserInfo();

            if (string.IsNullOrWhiteSpace(model.Username)) return View("NewActor", model);
            var user = model.Username;

            var obj = new ASObject();
            obj.Type.Add("https://www.w3.org/ns/activitystreams#Person");
            obj["preferredUsername"].Add(ASTerm.MakePrimitive(user));
            obj["name"].Add(ASTerm.MakePrimitive(string.IsNullOrWhiteSpace(model.Name) ? "Unnamed" : model.Name));
            if (!string.IsNullOrWhiteSpace(model.Summary))
                obj["summary"].Add(ASTerm.MakePrimitive(model.Summary));

            var create = new ASObject();
            create.Type.Add("https://www.w3.org/ns/activitystreams#Create");
            create["object"].Add(ASTerm.MakeSubObject(obj));
            create["to"].Add(ASTerm.MakeId("https://www.w3.org/ns/activitystreams#Public"));

            var stagingStore = new StagingEntityStore(_entityStore);
            var apo = await _flattener.FlattenAndStore(stagingStore, create);
            var handler = new CreateActorHandler(stagingStore, apo, null, null, User, _collectionTools, _entityData, _connection);
            await handler.Handle();

            var resultUser = await _entityStore.GetEntity(handler.MainObject.Data["object"].First().Id, false);
            var outbox = await _entityStore.GetEntity(resultUser.Data["outbox"].First().Id, false);
            var delivery = new DeliveryHandler(stagingStore, handler.MainObject, resultUser, outbox, User, _collectionTools, _provider.GetRequiredService<DeliveryService>());
            await delivery.Handle();

            return RedirectToAction("Edit", new { id = resultUser.Id });
        }

        public class BadgeTokenModel
        {
            public string Username { get; set; }
            public string Password { get; set; }
        }

        public class BadgeTokenResponse
        {
            public string Actor { get; set; }
            public string Token { get; set; }
        }
    }
}
