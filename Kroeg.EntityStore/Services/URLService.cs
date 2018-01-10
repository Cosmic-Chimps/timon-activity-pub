﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kroeg.ActivityStreams;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Kroeg.EntityStore.Store;

namespace Kroeg.Server.Tools
{
    public class URLService
    {
        private readonly ServerConfig _serverConfig;
        public URLService(ServerConfig serverConfig)
        {
            _serverConfig = serverConfig;
        }

        public IConfiguration EntityNames { private get; set; }

#region URL generation
        private string _getFormat(IEnumerable<string> type, string category, bool isRelative, string categoryTwo = null)
        {
            var firstformatType = type.FirstOrDefault(a => EntityNames[a.ToLower()] != null);
            if (firstformatType != null) return EntityNames[firstformatType.ToLower()];
            if (isRelative && EntityNames["+" + category] != null) return EntityNames["+" + category];
            if (categoryTwo != null && EntityNames["!" + categoryTwo] != null) return EntityNames["!" + categoryTwo];
            if (EntityNames["!" + category] != null) return EntityNames["!" + category];
            return EntityNames["!fallback"];
        }

        private static string _generateSlug(string val)
        {
            if (val == null) return null;
            val = val.ToLower().Substring(0, Math.Min(val.Length, 40));

            val = Regex.Replace(val, @"[^a-z0-9\s-]", "");
            val = Regex.Replace(val, @"\s+", " ");
            return Regex.Replace(val, @"\s", "-");
        }

        private static string _shortGuid() => Guid.NewGuid().ToString().Substring(0, 8);

        private async Task<JToken> _parse(IEntityStore store, JObject data, JToken curr, string thing)
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
                return curr ?? _shortGuid();
            if (thing == "lower")
                return curr?.ToObject<string>()?.ToLower();
            if (thing == "slug")
                return _generateSlug(curr?.ToObject<string>());
            if (thing.StartsWith("'"))
                return curr ?? thing.Substring(1);

            if (thing.All(char.IsNumber) && curr?.Type == JTokenType.Array) return ((JArray) curr)[int.Parse(thing)];

            return curr;
        }

        private async Task<string> _runCommand(IEntityStore store, JObject data, IEnumerable<string> args)
        {
            JToken val = null;
            foreach (var item in args)
            {
                val = await _parse(store, data, val, item);
            }

            return (val ?? "unknown").ToObject<string>();
        }

        private async Task<string> _parseUriFormat(IEntityStore store, JObject data, string format)
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
                    result.Append(format.Substring(index, nextEscape - index));
                    result.Append(format[nextEscape + 1]);
                    index = nextEscape + 2;
                }
                else if (nextStart < nextEscape && nextStart < format.Length)
                {
                    result.Append(format.Substring(index, nextStart - index));

                    var end = format.IndexOf("}", nextStart);
                    if (end == -1) throw new Exception("invalid format for URI");

                    var contents = format.Substring(nextStart + 2, end - nextStart - 2).Split('|');
                    result.Append(await _runCommand(store, data, contents));

                    index = end + 1;
                }
                else if (nextStart == int.MaxValue && nextEscape == int.MaxValue)
                {
                    result.Append(format.Substring(index));
                    break;
                }
            }

            return result.ToString();
        }

        private static string _append(string a, string b)
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

            var format = _getFormat(types, category, parentId != null, categoryTwo);
            var result = await _parseUriFormat(store, @object.Serialize(), format);
            if (parentId != null && result.StartsWith("+"))
                return _append(parentId, result.Substring(1).ToLower());

            result = result.ToLower();
            if (Uri.IsWellFormedUriString(result, UriKind.Absolute))
                return result;

            return _serverConfig.BaseUri + result.ToLower(); 
        }

        public async Task<string> FindUnusedID(IEntityStore entityStore, ASObject @object, string category = null, string parentId = null)
        {
            var types = @object.Type;
            var format = _getFormat(types, category, parentId != null);

            string uri = await UriFor(entityStore, @object, category, parentId);
            if (format.Contains("guid")) // is GUID-based, can just regenerate
            {
                while (await entityStore.GetEntity(uri, false) != null) uri = await UriFor(entityStore, @object, category, parentId);                
            }
            else if (await entityStore.GetEntity(uri, false) != null)
            {
                string shortGuid = _shortGuid();
                while (await entityStore.GetEntity($"{uri}-{shortGuid}", false) != null) shortGuid = _shortGuid();
                return $"{uri}-{shortGuid}";
            }

            return uri;
        }
#endregion
    }
}
