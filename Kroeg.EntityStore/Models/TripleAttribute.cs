using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kroeg.EntityStore.Models
{
    public class TripleAttribute
    {
        [Key]
        public int AttributeId { get; set; }

        public string Uri { get; set; }

        [InverseProperty("Id")]
        public List<APTripleEntity> Entities { get; set; }
    }
}
