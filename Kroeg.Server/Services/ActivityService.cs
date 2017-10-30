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
        private readonly RelevantEntitiesService _relevantEntities;

        private class OriginatingCreateJson
        {
            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("object")]
            public string Object { get; set; }
        }

        public ActivityService(RelevantEntitiesService relevantEntities)
        {
            _relevantEntities = relevantEntities;
        }

        public async Task<APEntity>
            DetermineOriginatingCreate(string id)
        {
            return (await _relevantEntities.FindRelevantObject("https://www.w3.org/ns/activitystreams#Create", id)).FirstOrDefault();
        }
    }
}
