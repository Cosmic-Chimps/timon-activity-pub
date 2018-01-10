using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kroeg.Server.Models
{
    public class WebSubClient
    {
        public int WebSubClientId { get; set; }

        public APTripleEntity ForUser { get; set; }
        public int ForUserId { get; set; }

        public APTripleEntity TargetUser { get; set; }
        public int TargetUserId { get; set; }

        public string Topic { get; set; }

        public DateTime Expiry { get; set; }
        public string Secret { get; set; }
    }
}
