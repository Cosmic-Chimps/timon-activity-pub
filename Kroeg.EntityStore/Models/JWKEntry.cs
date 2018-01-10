using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace Kroeg.EntityStore.Models
{
    public class JWKEntry
    {
        // IMPORTANT: Id and OwnerId together are the primary key here!

        public string Id { get; set; }

        public int OwnerId { get; set; }
        public APTripleEntity Owner { get; set; }

        public string SerializedData { get; set; }

        [NotMapped]
        public JsonWebKey Key => JsonConvert.DeserializeObject<JsonWebKey>(SerializedData);
    }
}
