using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Net.Http;
using Kroeg.ActivityStreams;
using Kroeg.Server.Configuration;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Store;
using Newtonsoft.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Kroeg.ActivityPub.ClientToServer;
using Kroeg.ActivityPub.Shared;
using System.Data.Common;
using Dapper;
using Kroeg.EntityStore.Services;
using Kroeg.ActivityPub.BackgroundTasks;
using Kroeg.Services;
using Kroeg.ActivityPub.Services;
using Microsoft.Extensions.Logging;
using Dapr;
using Kroeg.Server.Tos.Request;
using System.IdentityModel.Tokens.Jwt;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Controllers
{
  [Route("/message-bus")]
  [ApiController]
  public class MessageBusController : ControllerBase
  {
    private readonly DbConnection _connection;
    private readonly UserManager<APUser> _userManager;
    private readonly SignInManager<APUser> _signInManager;
    private readonly JwtTokenSettings _tokenSettings;
    private readonly EntityFlattener _entityFlattener;
    private readonly IEntityStore _entityStore;
    private readonly IServerConfig _serverConfig;
    private readonly IDataProtector _dataProtector;
    private readonly IConfiguration _configuration;
    private readonly DeliveryService _deliveryService;
    private readonly SignatureVerifier _verifier;
    private readonly IServiceProvider _provider;
    private readonly CollectionTools _collectionTools;
    private readonly RelevantEntitiesService _relevantEntities;
    readonly ILogger<MessageBusController> _log;

    public MessageBusController(DbConnection connection,
      UserManager<APUser> userManager,
      SignInManager<APUser> signInManager,
      JwtTokenSettings tokenSettings,
      EntityFlattener entityFlattener,
      IEntityStore entityStore,
      IServerConfig serverConfig,
      IDataProtectionProvider dataProtectionProvider,
      IConfiguration configuration,
      DeliveryService deliveryService,
      SignatureVerifier verifier,
      IServiceProvider provider,
      CollectionTools collectionTools,
      RelevantEntitiesService relevantEntities,
      ILogger<MessageBusController> log)
    {
      _connection = connection;
      _userManager = userManager;
      _signInManager = signInManager;
      _tokenSettings = tokenSettings;
      _entityFlattener = entityFlattener;
      _entityStore = entityStore;
      _serverConfig = serverConfig;
      _dataProtector = dataProtectionProvider.CreateProtector("OAuth tokens");
      _configuration = configuration;
      _deliveryService = deliveryService;
      _verifier = verifier;
      _provider = provider;
      _collectionTools = collectionTools;
      _relevantEntities = relevantEntities;
      _log = log;
    }


    [Topic("messagebus", "register-channel-activitypub")]
    [HttpPost("register-channel-activitypub")]
    public async Task<IActionResult> DoRegisterTimonChannelSub(RegisterRequest model)
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

      if (!ModelState.IsValid)
      {
        return BadRequest(ModelState);
      }

      await _connection.OpenAsync();
      using var trans = _connection.BeginTransaction();
      var result = await _userManager.CreateAsync(apuser, model.Password);

      if (!result.Succeeded)
      {
        ModelState.AddModelError("", result.Errors.First().Description);
      }

      if (!ModelState.IsValid)
      {
        return BadRequest(ModelState);

      }
      if (await _connection.ExecuteAsync("select count(*) from \"Users\"") == 1)
      {
        await _userManager.AddClaimAsync(apuser, new Claim("admin", "true"));
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

      _log.LogInformation($"--- creating actor. Unflattened:\n{create.Serialize().ToString(Formatting.Indented)}");

      var apo = await _entityFlattener.FlattenAndStore(_entityStore, create);

      _log.LogInformation($"Flat: {apo.Data.Serialize().ToString(Formatting.Indented)}\n----");

      var handler = new CreateActorHandler(_entityStore, apo, null, null, User, _collectionTools, _connection)
      {
        UserOverride = apuser.Id
      };

      await handler.Handle();

      var resultUser = await _entityStore.GetEntity(handler.MainObject.Data["object"].First().Id, false);
      var outbox = await _entityStore.GetEntity(resultUser.Data["outbox"].First().Id, false);
      var delivery = new DeliveryHandler(_entityStore, handler.MainObject, resultUser, outbox, User, _collectionTools, _provider.GetRequiredService<DeliveryService>());
      await delivery.Handle();

      trans.Commit();

      var activityPubUserId = handler.MainObject.Data["object"].First().Id;

      return Created(activityPubUserId, "");
    }

    [Topic("messagebus", "login-channel-activitypub")]
    [HttpPost("login-channel-activitypub")]
    public async Task<IActionResult> DoLogin(LoginRequest model)
    {
      var result = await _signInManager.PasswordSignInAsync(model.Username, model.Password, false, false);

      if (!result.Succeeded)
      {
        return BadRequest();
      }

      var user = await _userManager.FindByNameAsync(model.Username);

      var actorId = $"{_serverConfig.BaseUri}users/{model.Username}";

      var claims = new Claim[]
      {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id),
        new Claim(JwtTokenSettings.ActorClaim, actorId)
      };

      var jwt = new JwtSecurityToken(
          issuer: _tokenSettings.Issuer,
          audience: _tokenSettings.Audience,
          claims: claims,
          notBefore: DateTime.UtcNow,
          expires: DateTime.UtcNow.AddDays(1),
          signingCredentials: _tokenSettings.Credentials
      );

      var encodedJwt = new JwtSecurityTokenHandler().WriteToken(jwt);

      return Ok(new { Token = encodedJwt });
    }

    // [HttpPost("login-channel-activitypub")]
    // public async Task<IActionResult> VerifyAuthorize()
    // {
    //   if (!string.IsNullOrWhiteSpace(model.Deny))
    //   {
    //     return BuildRedir(model.RedirectUri, model.ResponseType, $"error=access_denied&state={model.State}");
    //   }

    //   var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
    //   var actor = await _entityStore.GetEntity(model.ActorID, false);
    //   var hasAccess = await _connection.ExecuteScalarAsync<bool>("select exists(select 1 from \"UserActorPermissions\" where \"UserId\" = @UserId and \"ActorId\" = @ActorId)", new { UserId = userId, ActorId = actor.DbId });
    //   model.Actor = actor;
    //   if (!hasAccess || !ModelState.IsValid)
    //   {
    //     return View("ChooseActorOAuth", model);
    //   }
    //   var exp = TimeSpan.FromSeconds(model.Expiry);
    //   if (exp > _tokenSettings.ExpiryTime)
    //   {
    //     exp = _tokenSettings.ExpiryTime;
    //   }

    //   var claims = new Claim[]
    //   {
    //     new Claim(JwtRegisteredClaimNames.Sub, User.FindFirstValue(ClaimTypes.NameIdentifier)),
    //     new Claim(JwtTokenSettings.ActorClaim, model.ActorID)
    //   };

    //   var jwt = new JwtSecurityToken(
    //       issuer: _tokenSettings.Issuer,
    //       audience: _tokenSettings.Audience,
    //       claims: claims,
    //       notBefore: DateTime.UtcNow,
    //       expires: DateTime.UtcNow.Add(exp),
    //       signingCredentials: _tokenSettings.Credentials
    //   );

    //   var encodedJwt = new JwtSecurityTokenHandler().WriteToken(jwt);

    //   if (model.ResponseType == "token")
    //   {
    //     return BuildRedir(model.RedirectUri, model.ResponseType, $"access_token={encodedJwt}&token_type=bearer&expires_in={(int)exp.TotalSeconds}&state={Uri.EscapeDataString(model.State ?? "")}");
    //   }
    //   else if (model.ResponseType == "code")
    //   {
    //     encodedJwt = _dataProtector.Protect(encodedJwt);

    //     return BuildRedir(model.RedirectUri, model.ResponseType, $"code={Uri.EscapeDataString(encodedJwt)}&state={Uri.EscapeDataString(model.State ?? "")}");
    //   }

    //   return StatusCode(500);
    // }
  }
}
