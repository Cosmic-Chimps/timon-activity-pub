namespace Kroeg.EntityStore.Models
{
    public class SalmonKey
    {
        public int SalmonKeyId { get; set; }
        public int EntityId { get; set; }
        public APTripleEntity Entity { get; set; }
        public string PrivateKey { get; set; }
    }
}
