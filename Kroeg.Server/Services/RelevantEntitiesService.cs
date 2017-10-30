using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Services
{
    public class RelevantEntitiesService
    {
        private readonly APContext _context;
        private readonly TripleEntityStore _entityStore;

        public RelevantEntitiesService(APContext context, TripleEntityStore entityStore)
        {
            _context = context;
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

            IQueryable<APTripleEntity> query = _context.TripleEntities;
            foreach (var item in attributeMapping)
            {
                query = query.Where(a => a.Triples.Any(b => b.SubjectId == a.IdId && b.PredicateId == item.Key && b.AttributeId == item.Value));
            }

            if (isOwner.HasValue)
                query = query.Where(a => a.IsOwner == isOwner.Value);

            return await _entityStore.GetEntities(await query.Select(a => a.EntityId).ToListAsync());
        }

        public async Task<List<APEntity>> FindRelevantObject(string authorId, string objectType, string objectId)
        {
            return await _search(new Dictionary<string, string> {
                ["rdf:type"] = objectType,
                ["https://www.w3.org/ns/activitystreams#object"] = objectType,
                ["https://www.w3.org/ns/activitystreams#actor"] = authorId
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
