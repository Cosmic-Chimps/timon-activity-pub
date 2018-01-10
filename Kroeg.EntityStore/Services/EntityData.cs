using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kroeg.ActivityStreams;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Kroeg.EntityStore.Store;

namespace Kroeg.EntityStore.Services
{
    public static class EntityData
    {
        private static readonly HashSet<string> Activities = new HashSet<string>
        {
            "https://www.w3.org/ns/activitystreams#Create", "https://www.w3.org/ns/activitystreams#Update",
            "https://www.w3.org/ns/activitystreams#Delete", "https://www.w3.org/ns/activitystreams#Follow",
            "https://www.w3.org/ns/activitystreams#Add", "https://www.w3.org/ns/activitystreams#Remove",
            "https://www.w3.org/ns/activitystreams#Like", "https://www.w3.org/ns/activitystreams#Block",
            "https://www.w3.org/ns/activitystreams#Undo", "https://www.w3.org/ns/activitystreams#Announce"
        };

        internal static readonly HashSet<string> Actors = new HashSet<string>
        {
            "https://www.w3.org/ns/activitystreams#Actor", "https://www.w3.org/ns/activitystreams#Application",
            "https://www.w3.org/ns/activitystreams#Group", "https://www.w3.org/ns/activitystreams#Organization",
            "https://www.w3.org/ns/activitystreams#Person", "https://www.w3.org/ns/activitystreams#Service"
        };

        [Obsolete("hardcoded single type")]
        public static bool IsActivity(string type)
        {
            return  Activities.Contains(type);
        }

        public static bool IsActivity(ASObject @object)
        {
            return @object["actor"].Count > 0;
        }

        public static bool IsActor(ASObject @object)
        {
            return @object.Type.Any(Actors.Contains);
        }
    }
}
