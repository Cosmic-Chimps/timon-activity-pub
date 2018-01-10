using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Kroeg.ActivityStreams;
using System;

namespace Kroeg.Server.Models
{
    public class APEntity
    {
        [Key]
        public string Id { get; set; }

        public string Type { get; set; }

        public DateTime Updated { get; set; }

        public bool IsOwner { get; set; }

        public ASObject Data { get; set; }

        public int DbId { get; set; }

        public static APEntity From(string id, ASObject @object)
        {
            var type = @object.Type.FirstOrDefault();
            if (type?.StartsWith("_") != false) type = "Unknown";

            @object.Id = id;
            return new APEntity
            {
                Id = @object.Id,
                Data = @object,
                Type = type,
                Updated = DateTime.Now
            };
        }

        public APEntity Clone()
        {
            return new APEntity { Id = Id, Data = Data.Clone(), Type = Type, Updated = Updated, IsOwner = IsOwner, DbId = DbId };
        }

        public static APEntity From(ASObject @object, bool isOwner = false)
        {
            var type = @object.Type.FirstOrDefault();
            if (type?.StartsWith("_") != false) type = "Unknown";

            return new APEntity
            {
                Id = @object.Id,
                Data = @object,
                Type = type,
                IsOwner = isOwner,
                Updated = DateTime.Now
            };
        }
    }
}
