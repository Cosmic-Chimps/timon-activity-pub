using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Kroeg.Server.Services
{
    public class ActivityService
    {
        private readonly APContext _context;
        private readonly TripleEntityStore _entityStore;

        private class OriginatingCreateJson
        {
            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("object")]
            public string Object { get; set; }
        }

        public ActivityService(APContext context, TripleEntityStore entityStore)
        {
            _context = context;
            _entityStore = entityStore;
        }

        public async Task<APEntity>
            DetermineOriginatingCreate(string id)
        {
            var type = await _entityStore.ReverseAttribute("https://www.w3.org/ns/activitystreams#Create", false);
            if (type == null) return null;
            
            var objectType = await _entityStore.ReverseAttribute("https://www.w3.org/ns/activitystreams#object", false);
            if (type == null) return null;

            var rdfType = await _entityStore.ReverseAttribute("rdf:type", false);
            if (type == null) return null;

            var objectId = await _entityStore.ReverseAttribute(id, false);
            if (objectId == null) return null;

            var firstResult = await _context.TripleEntities.FirstOrDefaultAsync(a =>
                a.Triples.Any(b => b.SubjectId == a.IdId && b.PredicateId == rdfType.Value && b.AttributeId == type.Value) &&
                a.Triples.Any(b => b.SubjectId == a.IdId && b.PredicateId == objectType.Value && b.AttributeId == objectId.Value)
                );
            
            if (firstResult == null)
                return null;
            
            return await _entityStore.GetEntity(firstResult.EntityId);
        }
    }
}
