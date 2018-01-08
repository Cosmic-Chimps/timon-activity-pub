using System;
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

        [HttpGet("webfinger")]
        public async Task<IActionResult> WebFinger(string resource)
        {
            if (!resource.StartsWith("acct:")) return Unauthorized();

            var username = resource.Split(':')[1].Split('@');

            var items = await _relevantEntities.FindEntitiesWithPreferredUsername(username[0]);
            if (items.Count == 0) return NotFound();

            var item = items.First();

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
