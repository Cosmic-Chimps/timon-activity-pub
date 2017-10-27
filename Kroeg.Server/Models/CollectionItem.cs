using System.ComponentModel.DataAnnotations;

namespace Kroeg.Server.Models
{
    public class CollectionItem
    {
        [Key]
        public int CollectionItemId { get; set; }

        public string CollectionId { get; set; }
        public APDBEntity Collection { get; set; }

        public string ElementId { get; set; }
        public APDBEntity Element { get; set; }

        public bool IsPublic { get; set; }
    }
}
