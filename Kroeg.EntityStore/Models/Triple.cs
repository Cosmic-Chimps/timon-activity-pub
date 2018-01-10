namespace Kroeg.EntityStore.Models
{
    public class Triple
    {
        public int TripleId { get; set; }

        public int SubjectId { get; set; }
        public TripleAttribute Subject { get; set; }

        public int SubjectEntityId { get; set; }
        public APTripleEntity SubjectEntity { get; set; }
        
        public int PredicateId { get; set; }
        public TripleAttribute Predicate { get; set; }

        public int? AttributeId { get; set; }
        public TripleAttribute Attribute { get; set; }

        public string Object { get; set; }

        public int? TypeId { get; set; }
        public TripleAttribute Type { get; set; } 

        // subject[predicate] = object;
    }
}
