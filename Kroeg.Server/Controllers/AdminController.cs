using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Kroeg.EntityStore.Models;
using Kroeg.ActivityStreams;
using Kroeg.EntityStore.Store;
using Kroeg.Server.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using System.IO;
using Dapper;
using System.Data.Common;
using Kroeg.EntityStore.Services;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Controllers
{
  [Route("admin"), Authorize("admin")]
  public class AdminController : Controller
  {
    private readonly DbConnection _connection;
    private readonly IEntityStore _entityStore;
    private readonly JwtTokenSettings _tokenSettings;
    private readonly SignInManager<APUser> _signInManager;
    private readonly IServiceProvider _provider;
    private readonly IConfiguration _configuration;
    private readonly EntityFlattener _flattener;
    private readonly UserManager<APUser> _userManager;
    private readonly RelevantEntitiesService _relevantEntities;
    private readonly CollectionTools _collectionTools;

    public AdminController(DbConnection connection, IEntityStore entityStore, JwtTokenSettings tokenSettings, SignInManager<APUser> signInManager, IServiceProvider provider, IConfiguration configuration, EntityFlattener flattener, UserManager<APUser> userManager, RelevantEntitiesService relevantEntities, CollectionTools collectionTools)
    {
      _connection = connection;
      _entityStore = entityStore;
      _tokenSettings = tokenSettings;
      _signInManager = signInManager;
      _provider = provider;
      _configuration = configuration;
      _flattener = flattener;
      _userManager = userManager;
      _relevantEntities = relevantEntities;
      _collectionTools = collectionTools;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("complete")]
    public async Task<IActionResult> Autocomplete(string id)
    {
      _connection.Open();

      var attributes = await _connection.QueryAsync<TripleAttribute>("select * from \"TripleAttributes\" where \"Uri\" like @str limit 10", new { str = id + "%" });
      return Json(attributes.Select(a => a.Uri).ToList());
    }

    [HttpGet("entity")]
    public async Task<IActionResult> GetEntity(string id)
    {
      var entity = await _entityStore.GetEntity(id, true);
      if (entity == null) return NotFound();

      return Content(entity.Data.Serialize().ToString(), "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"");
    }

    [HttpPost("entity")]
    public async Task<IActionResult> PostEntity(string id)
    {
      string data;
      using (var reader = new StreamReader(Request.Body))
        data = await reader.ReadToEndAsync();
      var entity = await _entityStore.GetEntity(id, true);
      if (entity == null) return NotFound();

      entity.Data = ASObject.Parse(data);
      await _entityStore.StoreEntity(entity);

      return Ok();
    }
  }
}
