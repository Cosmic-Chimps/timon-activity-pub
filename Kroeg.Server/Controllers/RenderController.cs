using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.EntityStore.Store;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Kroeg.Server.Services.Template;
using System.Linq;
using System;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Kroeg.Server.Services;

namespace Kroeg.Server.Controllers
{
    [Route("/render")]
    public class RenderController : Controller
    {
        private readonly IEntityStore _entityStore;
        private readonly TemplateService _templateService;

        public RenderController(IEntityStore entityStore, TemplateService templateService, CollectionTools collectionTools)
        {
            _entityStore = new CollectionEntityStore(collectionTools, entityStore);
            _templateService = templateService;
        }

        [Authorize("pass"), HttpGet("refresh")]
        public IActionResult Refresh()
        {
            _templateService.Parse();
            return Ok("done");
        }

        [HttpGet("context")]
        public IActionResult GetContext()
        {
            return Content(new JObject { ["@context"] = JsonLDConfig.GetContext(false) }.ToString(), "application/ld+json");
        }

        [HttpGet("remote")]
        public async Task<IActionResult> RenderRemote(string url)
        {
            if (url == null)
                return RedirectPermanent("/");

            var obj = await _entityStore.GetEntity(url, true);

            var regs = new TemplateService.Registers();
            regs.UsedEntities[obj.Id] = obj;

            var text = await _templateService.ParseTemplate("body", _entityStore, obj, regs);

            var objectTemplates = regs.UsedEntities.Select(a => new Tuple<string, JToken>(a.Key, a.Value.Data.Serialize(true))).ToDictionary(a => a.Item1, a => a.Item2);
            if (Request.Query.ContainsKey("nopreload")) objectTemplates.Clear();

            var page = _templateService.PageTemplate.Replace("<div class=\"container\" x-render=\"body\"></div>", text).Replace("{{preload}}", JsonConvert.SerializeObject(objectTemplates).Replace("<", "\\u003C"));

            return Content(page, "text/html");
        }

        [HttpGet("")]
        public async Task<IActionResult> Render()
        {
            if (!HttpContext.Items.ContainsKey("object"))
                return RedirectPermanent("/");

            var obj = (APEntity) HttpContext.Items["object"];
            var regs = new TemplateService.Registers();
            regs.UsedEntities[obj.Id] = obj;

            var text = await _templateService.ParseTemplate("body", _entityStore, obj, regs);

            var objectTemplates = regs.UsedEntities.Select(a => new Tuple<string, JToken>(a.Key, a.Value.Data.Serialize(true))).ToDictionary(a => a.Item1, a => a.Item2);
            if (Request.Query.ContainsKey("nopreload")) objectTemplates.Clear();

            var page = _templateService.PageTemplate.Replace("<div class=\"container\" x-render=\"body\"></div>", text).Replace("{{preload}}", JsonConvert.SerializeObject(objectTemplates).Replace("<", "\\u003C"));

            return Content(page, "text/html");
        }
    }
}
