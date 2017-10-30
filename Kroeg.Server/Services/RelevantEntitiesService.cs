using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.Server.Services.EntityStore;
using System.Data;
using System.Data.Common;
using Dapper;

namespace Kroeg.Server.Services
{
    public class RelevantEntitiesService
    {
        private readonly DbConnection _connection;
        private readonly TripleEntityStore _entityStore;

        public RelevantEntitiesService(TripleEntityStore entityStore, DbConnection connection)
        {
            _connection = connection;
            _entityStore = entityStore;
        }

        private class RelevantObjectJson
        {
            [JsonProperty("type")]
            public string Type { get; set; }
            [JsonProperty("object")]
            public string Object { get; set; }
            [JsonProperty("actor")]
            public string Actor { get; set; }
        }

        private class PreferredUsernameJson
        {
            [JsonProperty("preferredUsername")]
            public string PreferredUsername { get; set; }
        }

        private class FollowerJson
        {
            [JsonProperty("followers")]
            public string Followers { get; set; }
        }

        private async Task<List<APEntity>> _search(Dictionary<string, string> lookFor, bool? isOwner = null)
        {
            var attributeMapping = new Dictionary<int, int>();
            foreach (var val in lookFor)
            {
                var reverseId = await _entityStore.ReverseAttribute(val.Key, false);
                if (reverseId == null) return new List<APEntity>();

                var reverseVal = await _entityStore.ReverseAttribute(val.Value, false);
                if (reverseVal == null) return new List<APEntity>();

                attributeMapping[reverseId.Value] = reverseVal.Value;
            }

            var miniTables = string.Join(", ", attributeMapping.Select(a => $"attribute_{a.Key} as (SELECT \"SubjectId\" as subj, \"SubjectEntityId\" as subje from \"Triples\" where \"PredicateId\" = {a.Key} and \"AttributeId\" = {a.Value})"));

            var start = $"select a.* from \"TripleEntities\" a where " + string.Join(" and ", attributeMapping.Select(a => $"exists(select 1 from attribute_{a.Key} where subj = a.\"IdId\" and subje = a.\"EntityId\")"));

            if (isOwner.HasValue)
                start += " and a.\"IsOwner\" = " + (isOwner.Value ? "TRUE" : "FALSE");

            return await _entityStore.GetEntities((await _connection.QueryAsync<APTripleEntity>(start)).Select(a => a.EntityId).ToList());
        }

        public async Task<List<APEntity>> FindRelevantObject(string authorId, string objectType, string objectId)
        {
            return await _search(new Dictionary<string, string> {
                ["rdf:type"] = objectType,
                ["https://www.w3.org/ns/activitystreams#object"] = objectType,
                ["https://www.w3.org/ns/activitystreams#actor"] = authorId
            });
        }

        public async Task<List<APEntity>> FindRelevantObject(string objectType, string objectId)
        {
            return await _search(new Dictionary<string, string> {
                ["rdf:type"] = objectType,
                ["https://www.w3.org/ns/activitystreams#object"] = objectType
            });
        }

        public async Task<List<APEntity>> FindEntitiesWithFollowerId(string followerId)
        {
            return await _search(new Dictionary<string, string> {
                ["https://www.w3.org/ns/activitystreams#followers"] = followerId
            });
        }

        public async Task<List<APEntity>> FindEntitiesWithPreferredUsername(string username)
        {
            return await _search(new Dictionary<string, string> {
                ["https://www.w3.org/ns/activitystreams#preferredUsername"] = username
            }, true);
        }
    }
}
