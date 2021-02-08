using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kroeg.ActivityStreams;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Kroeg.EntityStore.Store;

namespace Kroeg.EntityStore.Services
{
  public class URLService
  {
    private readonly IServerConfig _serverConfig;
    public URLService(IServerConfig serverConfig, IConfiguration configuration)
    {
      _serverConfig = serverConfig;
      EntityNames = configuration.GetSection("EntityNames");
    }

    public IConfiguration EntityNames { private get; set; }

    #region URL generation
    private string GetFormat(IEnumerable<string> type, string category, bool isRelative, string categoryTwo = null)
    {
      var firstformatType = type.FirstOrDefault(a => EntityNames[a.ToLower()] != null);
      if (firstformatType != null) return EntityNames[firstformatType.ToLower()];
      if (isRelative && EntityNames["+" + category] != null) return EntityNames["+" + category];
      if (categoryTwo != null && EntityNames["!" + categoryTwo] != null) return EntityNames["!" + categoryTwo];
      if (EntityNames["!" + category] != null) return EntityNames["!" + category];
      return EntityNames["!fallback"];
    }

    private static string GenerateSlug(string val)
    {
      if (val == null) return null;
      val = val.ToLower().Substring(0, Math.Min(val.Length, 40));

      val = Regex.Replace(val, @"[^a-z0-9\s-]", "");
      val = Regex.Replace(val, @"\s+", " ");
      return Regex.Replace(val, @"\s", "-");
    }

    private static string ShortGuid() => Guid.NewGuid().ToString().Substring(0, 8);

    private async Task<JToken> Parse(IEntityStore store, JObject data, JToken curr, string thing)
    {
      if (thing.StartsWith("$"))
        return curr ?? data.SelectToken(thing);
      if (thing.StartsWith("%"))
        return curr?.SelectToken(thing.Replace('%', '$'));
      if (thing == "resolve")
        return curr == null ? null : (await store?.GetEntity(curr.ToObject<string>(), false))?.Data?.Serialize();
      if (thing == "guid")
        return curr ?? Guid.NewGuid().ToString();
      if (thing == "shortguid")
        return curr ?? ShortGuid();
      if (thing == "lower")
        return curr?.ToObject<string>()?.ToLower();
      if (thing == "slug")
        return GenerateSlug(curr?.ToObject<string>());
      if (thing.StartsWith("'"))
        return curr ?? thing.Substring(1);

      if (thing.All(char.IsNumber) && curr?.Type == JTokenType.Array) return ((JArray)curr)[int.Parse(thing)];

      return curr;
    }

    private async Task<string> RunCommand(IEntityStore store, JObject data, IEnumerable<string> args)
    {
      JToken val = null;
      foreach (var item in args)
      {
        val = await Parse(store, data, val, item);
      }

      return (val ?? "unknown").ToObject<string>();
    }

    private async Task<string> ParseUriFormat(IEntityStore store, JObject data, string format)
    {
      var result = new StringBuilder();
      var index = 0;
      while (index < format.Length)
      {
        var nextEscape = format.IndexOf("\\", index);
        if (nextEscape == -1) nextEscape = int.MaxValue;
        var nextStart = format.IndexOf("${", index);
        if (nextStart == -1) nextStart = int.MaxValue;
        if (nextEscape < nextStart && nextEscape < format.Length)
        {
          result.Append(format[index..nextEscape]);
          result.Append(format[nextEscape + 1]);
          index = nextEscape + 2;
        }
        else if (nextStart < nextEscape && nextStart < format.Length)
        {
          result.Append(format[index..nextStart]);

          var end = format.IndexOf("}", nextStart);
          if (end == -1) throw new Exception("invalid format for URI");

          var contents = format.Substring(nextStart + 2, end - nextStart - 2).Split('|');
          result.Append(await RunCommand(store, data, contents));

          index = end + 1;
        }
        else if (nextStart == int.MaxValue && nextEscape == int.MaxValue)
        {
          result.Append(format[index..]);
          break;
        }
      }

      return result.ToString();
    }

    private static string Append(string a, string b)
    {
      if (a.EndsWith("/"))
        return a + b;
      return a + "/" + b;
    }

    public async Task<string> UriFor(IEntityStore store, ASObject @object, string category = null, string parentId = null)
    {
      var types = @object.Type;

      if (category == null)
        if (@object["actor"].Any())
          category = "activity";
        else if (types.Any(a => EntityData.Actors.Contains(a)))
          category = "actor";
        else
          category = "object";

      string categoryTwo = null;
      if (@object["object"].Any(a => a.SubObject != null))
        categoryTwo = "create" + @object["object"].First().SubObject.Type.First().Split('#')[1];

      var format = GetFormat(types, category, parentId != null, categoryTwo);
      var result = await ParseUriFormat(store, @object.Serialize(), format);
      if (parentId != null && result.StartsWith("+"))
        return Append(parentId, result.Substring(1).ToLower());

      result = result.ToLower();
      if (Uri.IsWellFormedUriString(result, UriKind.Absolute))
        return result;

      return _serverConfig.BaseUri + result.ToLower();
    }

    public async Task<string> FindUnusedID(IEntityStore entityStore, ASObject @object, string category = null, string parentId = null)
    {
      var types = @object.Type;
      var format = GetFormat(types, category, parentId != null);

      string uri = await UriFor(entityStore, @object, category, parentId);
      if (format.Contains("guid")) // is GUID-based, can just regenerate
      {
        while (await entityStore.GetEntity(uri, false) != null) uri = await UriFor(entityStore, @object, category, parentId);
      }
      else if (await entityStore.GetEntity(uri, false) != null)
      {
        string shortGuid = ShortGuid();
        while (await entityStore.GetEntity($"{uri}-{shortGuid}", false) != null) shortGuid = ShortGuid();
        return $"{uri}-{shortGuid}";
      }

      return uri;
    }
    #endregion
  }
}
