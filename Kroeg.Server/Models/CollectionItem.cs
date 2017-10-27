using System.ComponentModel.DataAnnotations;

namespace Kroeg.Server.Models
{
    public class CollectionItem
    {
        [Key]
        public int CollectionItemId { get; set; }

        public int CollectionId { get; set; }
        public APTripleEntity Collection { get; set; }

        public int ElementId { get; set; }
        public APTripleEntity Element { get; set; }

        public bool IsPublic { get; set; }
    }
}
