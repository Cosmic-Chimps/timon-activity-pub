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
    public class ServerConfig
    {
        public ServerConfig(IConfigurationSection kroegSection)
        {
            _kroegSection = kroegSection;
        }

        private readonly IConfigurationSection _kroegSection;
        public string BaseUri => _kroegSection["BaseUri"];
        public string BaseDomain => (new Uri(BaseUri)).Host;
        public string BasePath => (new Uri(BaseUri)).AbsolutePath;


        public bool RewriteRequestScheme => _kroegSection["RewriteRequestScheme"] == "True";
        public bool UnflattenRemotely => _kroegSection["UnflattenRemotely"] == "True";

        public static readonly List<Type> ServerToServerHandlers = new List<Type>();
        public static readonly List<Type> ClientToServerHandlers = new List<Type>();
        public static readonly List<IConverterFactory> Converters = new List<IConverterFactory>();
    }
}
