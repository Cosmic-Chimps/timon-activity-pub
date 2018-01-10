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
    [Route("/oauth")]
    public class OAuthController : Controller
    {
        private readonly DbConnection _connection;
        private readonly JwtTokenSettings _tokenSettings;
        private readonly IEntityStore _entityStore;
        private readonly IDataProtector _dataProtector;

        public OAuthController(DbConnection connection, JwtTokenSettings tokenSettings, IEntityStore entityStore, IDataProtectionProvider dataProtectionProvider)
        {
            _connection = connection;
            _tokenSettings = tokenSettings;
            _entityStore = entityStore;
            _dataProtector = dataProtectionProvider.CreateProtector("OAuth tokens");
        }

        public class OAuthActorModel
        {
            public List<APEntity> Actors { get; set; }
            public APEntity Actor { get; set; }
            public string ActorID { get; set; }
            public string ResponseType { get; set; }
            public string RedirectUri { get; set; }
            public string State { get; set; }
            public int Expiry { get; set; }
            public string Deny { get; set; }
        }



        private async Task<List<APEntity>> _getUserInfo()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var actors = (await _connection.QueryAsync<UserActorPermission>("SELECT * from \"UserActorPermissions\" where \"UserId\" = @Id", new { Id = userId })).ToList();
            var entities = new List<APEntity>();
            foreach (var actor in actors)
            {
                entities.Add(await _entityStore.GetDBEntity(actor.ActorId));
            }

            return entities;
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

        private RedirectResult _buildRedir(string baseUri, string response_type, string data)
        {
            if (response_type == "token")
                return RedirectPermanent(baseUri.Contains("#") ? (baseUri + "&" + data) : (baseUri + "#" + data));
            else
                return RedirectPermanent(_appendToUri(baseUri, data));
        }

        [Authorize("pass"), HttpGet("authorize")]
        public async Task<IActionResult> DoAuthorize(string id, string response_type, string redirect_uri, string state)
        {
            if (redirect_uri == null) return Ok("Could not find redirect url. What's going on?");
            if (response_type != "token" && response_type != "code") return _buildRedir(redirect_uri, response_type, "error=unsupported_response_type");
            if (User == null || User.FindFirstValue(ClaimTypes.NameIdentifier) == null) return RedirectToAction("Login", new { redirect = Request.Path.Value + Request.QueryString });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (id == null) return View("ListActor", new OAuthActorModel { ResponseType = response_type, RedirectUri = redirect_uri, State = state, Actors = await _getUserInfo(), Expiry = (int) _tokenSettings.ExpiryTime.TotalSeconds });

            var actor = await _entityStore.GetEntity(id, false);
            var hasAccess = await _connection.ExecuteScalarAsync<bool>("select exists(select 1 from \"UserActorPermissions\" where \"UserId\" = @UserId and \"ActorId\" = @ActorId)", new { UserId = userId, ActorId = actor.DbId });
            if (!hasAccess || actor == null || !actor.IsOwner)
            {
                return _buildRedir(redirect_uri, response_type, $"error=invalid_request&state={state}");
            }

            return View("ChooseActor", new OAuthActorModel { Actor = actor, ResponseType = response_type, RedirectUri = redirect_uri, State = state, Expiry = (int) _tokenSettings.ExpiryTime.TotalSeconds});
        }

        [Authorize("pass"), HttpPost("authorize"), ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyAuthorize(OAuthActorModel model)
        {
            if (!string.IsNullOrWhiteSpace(model.Deny))
                return _buildRedir(model.RedirectUri, model.ResponseType, $"error=access_denied&state={model.State}");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var actor = await _entityStore.GetEntity(model.ActorID, false);
            var hasAccess = await _connection.ExecuteScalarAsync<bool>("select exists(select 1 from \"UserActorPermissions\" where \"UserId\" = @UserId and \"ActorId\" = @ActorId)", new { UserId = userId, ActorId = actor.DbId });
            model.Actor = actor;
            if (!hasAccess || !ModelState.IsValid) return View("ChooseActorOAuth", model);
            var exp = TimeSpan.FromSeconds(model.Expiry);
            if (exp > _tokenSettings.ExpiryTime)
                exp = _tokenSettings.ExpiryTime;

            var claims = new Claim[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, User.FindFirstValue(ClaimTypes.NameIdentifier)),
                new Claim(JwtTokenSettings.ActorClaim, model.ActorID)
            };
            
            var jwt = new JwtSecurityToken(
                issuer: _tokenSettings.Issuer,
                audience: _tokenSettings.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.Add(exp),
                signingCredentials: _tokenSettings.Credentials
                );

            var encodedJwt = new JwtSecurityTokenHandler().WriteToken(jwt);

            if (model.ResponseType == "token")
                return _buildRedir(model.RedirectUri, model.ResponseType, $"access_token={encodedJwt}&token_type=bearer&expires_in={(int) exp.TotalSeconds}&state={Uri.EscapeDataString(model.State ?? "")}");
            else if (model.ResponseType == "code")
            {
                encodedJwt = _dataProtector.Protect(encodedJwt);

                return _buildRedir(model.RedirectUri, model.ResponseType, $"code={Uri.EscapeDataString(encodedJwt)}&state={Uri.EscapeDataString(model.State ?? "")}");
            }

            return StatusCode(500);
        }

        public class OAuthTokenModel
        {
            public string grant_type { get; set; }
            public string code { get; set; }
            public string redirect_uri { get; set; }
            public string client_id { get; set; }
        }

        public class JsonError
        {
            public string error { get; set; }
        }

        public class JsonResponse
        {
            public string access_token { get; set; }
            public string token_type { get; set; }
            public int expires_in { get; set; }
        }

        [HttpPost("token")]
        public IActionResult OAuthToken(OAuthTokenModel model)
        {
            if (model.grant_type != "authorization_code") return Json(new JsonError { error = "invalid_request" });
            try
            {
                var decrypted = _dataProtector.Unprotect(model.code);
                return Json(new JsonResponse {
                    access_token = decrypted,
                    expires_in = (int) _tokenSettings.ExpiryTime.TotalSeconds,
                    token_type = "bearer"
                });
            }
            catch (CryptographicException)
            {
                return Json(new JsonError { error = "invalid_request" });
            }
        }
    }
}
