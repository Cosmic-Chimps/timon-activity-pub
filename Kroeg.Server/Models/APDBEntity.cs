using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Kroeg.ActivityStreams;
using System;
using Newtonsoft.Json;

namespace Kroeg.Server.Models
{
    public class APDBEntity
    {
        [Key]
        public string Id { get; set; }

        [Column(TypeName = "jsonb")]
        public string SerializedData { get; set; }

        public string Type { get; set; }

        public DateTime Updated { get; set; }

        public bool IsOwner { get; set; }

        [NotMapped]
        public APEntity Entity
        {
            get => new APEntity {
                Data = ASObject.Parse(SerializedData, true),
                Id = Id,
                Type = Type,
                Updated = Updated,
                IsOwner = IsOwner
            };

            set {
                Id = value.Id;
                Type = value.Type;
                Updated = value.Updated;
                IsOwner = value.IsOwner;
                SerializedData = value.Data.Serialize(false).ToString(Formatting.None);
            }
        }
    }
}
