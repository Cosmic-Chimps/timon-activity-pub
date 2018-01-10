using System;

namespace Kroeg.EntityStore.Models
{
    public class WebsubSubscription
    {
        public int Id { get; set; }

        public DateTime Expiry { get; set; }
        public string Callback { get; set; }
        public string Secret { get; set; }

        public int UserId { get; set; }
        public APTripleEntity User { get; set; }
    }
}
