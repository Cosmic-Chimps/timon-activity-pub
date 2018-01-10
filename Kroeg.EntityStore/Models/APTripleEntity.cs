using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Kroeg.ActivityStreams;
using System;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Kroeg.Server.Models
{
    public class APTripleEntity
    {
        public int IdId { get; set; }
        public TripleAttribute Id { get; set; }

        [Key]
        public int EntityId { get; set; }

        public string Type { get; set; }

        public DateTime Updated { get; set; }

        public bool IsOwner { get; set; }

        [InverseProperty("SubjectEntity")]
        public List<Triple> Triples { get; set; }

/*
        [NotMapped]
        public APEntity Entity
        {
            get => new APEntity {
                Data = null,
                Type = Type,
                Updated = Updated,
                IsOwner = IsOwner
            };

            set {
                Type = value.Type;
                Updated = value.Updated;
                IsOwner = value.IsOwner;
            }
        }
        */
    }
}
