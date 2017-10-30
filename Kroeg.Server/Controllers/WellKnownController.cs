﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Net.Http;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Kroeg.Server.Services;
using System.Data;
using Dapper;
using System.Data.Common;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Controllers
{
    [Route(".well-known")]
    public class WellKnownController : Controller
    {
        private readonly IEntityStore _entityStore;
        private readonly EntityData _entityData;
        private readonly RelevantEntitiesService _relevantEntities;
        private readonly KeyService _keyService;
        private readonly DbConnection _connection;

        public WellKnownController(IEntityStore entityStore, EntityData entityData, RelevantEntitiesService relevantEntities, KeyService keyService, DbConnection connection)
        {
            _entityStore = entityStore;
            _entityData = entityData;
            _relevantEntities = relevantEntities;
            _keyService = keyService;
            _connection = connection;
        }

        public class WebfingerLink
        {
            public string rel { get; set; }
            public string type { get; set; }
            public string href { get; set; }
            public string template { get; set; }
        }

        public class WebfingerResult
        {
            public string subject { get; set; }
            public List<string> aliases { get; set; }
            public List<WebfingerLink> links { get; set; }
        }

        [HttpPost("hub")]
        public async Task<IActionResult> ProcessPushRequest()
        {
            var userId = (string) HttpContext.Items["fullPath"];
            var user = await _entityStore.GetEntity(userId, false);
            if (user == null)
                return StatusCode(400, "this is not a valid hub");

            var callback = Request.Form["hub.callback"].First();
            var mode = Request.Form["hub.mode"].First();
            var topic = Request.Form["hub.topic"].First();
            var lease_seconds = Request.Form["hub.lease_seconds"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(lease_seconds)) lease_seconds = "86400";
            var secret = Request.Form["hub.secret"].FirstOrDefault();

            if (mode != "unsubscribe" && mode != "subscribe")
                return StatusCode(400, "bad hub.mode");

            await _continueVerify(mode, callback, topic, lease_seconds, secret, user);
            return Accepted();
        }

        private async Task _continueVerify(string mode, string callback, string topic, string lease_seconds, string secret, APEntity user)
        {
            await Task.Delay(2000);
            var hc = new HttpClient();

            var testurl = callback;
            if (callback.Contains("?"))
                testurl += "&";
            else
                testurl += "?";


            string challenge = Guid.NewGuid().ToString();
            testurl += $"hub.mode={mode}&hub.topic={Uri.EscapeDataString(topic)}&hub.lease_seconds={lease_seconds}&hub.challenge={Uri.EscapeDataString(challenge)}";

            var result = await hc.GetAsync(testurl);
            if (!result.IsSuccessStatusCode)
                return;

            if (await result.Content.ReadAsStringAsync() != challenge)
                return;

            WebsubSubscription subscription = await _connection.QueryFirstOrDefaultAsync<WebsubSubscription>("select * from \"WebsubSubscriptions\" \"Callback\" = @Callback", new { Callback = callback });

            if (subscription != null)
            {
                if (mode == "unsubscribe")
                {
                    await _connection.ExecuteAsync("delete from \"WebsubSubscriptions\" where \"Id\" = @Id", new { Id = subscription.Id });
                }
                else
                {
                    subscription.Expiry = DateTime.Now.AddSeconds(int.Parse(lease_seconds ?? "86400"));
                    subscription.Secret = secret;
                    await _connection.ExecuteAsync("update \"WebsubSubscriptions\" set \"Expiry\"=@Expiry, \"Secret\"=@Secret where \"Id\" = @Id", subscription);
                }
            }
            else if (mode == "subscribe")
            {
                subscription = new WebsubSubscription()
                {
                    Callback = callback,
                    Expiry = DateTime.Now.AddSeconds(int.Parse(lease_seconds ?? "86400")),
                    Secret = secret,
                    UserId = user.DbId
                };

                await _connection.ExecuteAsync("insert into \"WebsubSubscriptions\" (\"Callback\", \"Expiry\", \"Secret\", \"UserId\") values (@Callback, @Expiry, @Secret, @UserId)", subscription);
            }
        }

        [HttpGet("webfinger")]
        public async Task<IActionResult> WebFinger(string resource)
        {
            if (!resource.StartsWith("acct:")) return Unauthorized();

            var username = resource.Split(':')[1].Split('@');

            var items = await _relevantEntities.FindEntitiesWithPreferredUsername(username[0]);
            if (items.Count == 0) return NotFound();

            var item = items.First();

            var outbox = item.Data["outbox"].First().Id + $".atom?from_id={int.MaxValue}";
            var inbox = item.Data["inbox"].First().Id;

            var result = new WebfingerResult()
            {
                subject = resource,
                aliases = new List<string>() { item.Id },
                links = new List<WebfingerLink>
                {
                    new WebfingerLink
                    {
                        rel = "http://webfinger.net/rel/profile-page",
                        type = "text/html",
                        href = item.Id
                    },

                    new WebfingerLink
                    {
                        rel = "http://schemas.google.com/g/2010#updates-from",
                        type = "application/atom+xml",
                        href = outbox
                    },

                    new WebfingerLink
                    {
                        rel = "self",
                        type = "application/activity+json",
                        href = item.Id
                    },

                    new WebfingerLink
                    {
                        rel = "self",
                        type = "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"",
                        href = item.Id
                    },

                    new WebfingerLink
                    {
                        rel = "salmon",
                        href = inbox
                    },

                    new WebfingerLink
                    {
                        rel = "http://ostatus.org/schema/1.0/subscribe",
                        template = item.Id + "?subscribe&user={uri}"
                    }
                }
            };

            var salmon = await _keyService.GetKey(item.Id);
            var magicKey = new Salmon.MagicKey(salmon.PrivateKey);

            result.links.Add(new WebfingerLink
                {
                    rel = "magic-public-key",
                    href = "data:application/magic-public-key," + magicKey.PublicKey
                });

            return Json(result);
        }

        [HttpGet("host-meta")]
        public IActionResult GetHostMeta()
        {
            Response.ContentType = "application/xrd+xml";

            var domain = Request.Host.ToUriComponent();

            return Ok("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
"<XRD xmlns=\"http://docs.oasis-open.org/ns/xri/xrd-1.0\">" +
$" <Link rel=\"lrdd\" type=\"application/jrd+json\" template=\"https://{domain}/.well-known/webfinger?resource={{uri}}\"/>" +
"</XRD>");
        }
    }
}
