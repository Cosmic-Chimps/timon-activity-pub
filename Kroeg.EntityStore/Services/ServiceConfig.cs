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
    public interface IServerConfig
    {
        string BaseUri { get; }
        string BaseUriWithoutTrailing { get; }
        string BaseDomain { get; }
        string BasePath { get; }
        bool RewriteRequestScheme { get; }
        bool UnflattenRemotely { get; }
        string TokenSigningKey { get; }
        bool CanRegister { get; }
        string FileUploadPath { get; }
        string FileUploadUrl { get; }
    }

    public class ServerConfig : IServerConfig
    {
        readonly string _baseUri;
        readonly string _tokenSigningKey;
        readonly bool _canRegister;
        readonly string _fileUploadPath;
        readonly string _fileUploadUrl;
        readonly bool _rewriteRequestScheme;
        readonly bool _unflattenRemotely;
        private readonly IConfigurationSection _kroegSection;

        public ServerConfig(IConfigurationSection kroegSection)
        {
            _kroegSection = kroegSection;
            _baseUri = _kroegSection["BaseUri"];
            _tokenSigningKey = _kroegSection["TokenSigningKey"];
            if (bool.TryParse(_kroegSection["CanRegister"], out bool val))
            {
                _canRegister = val;
            }
            _fileUploadPath = _kroegSection["FileUploadPath"];
            _fileUploadUrl = _kroegSection["FileUploadUrl"];

            if (bool.TryParse(_kroegSection["RewriteRequestScheme"], out bool val1))
            {
                _rewriteRequestScheme = val1;
            }

            if (bool.TryParse(_kroegSection["UnflattenRemotely"], out bool val2))
            {
                _unflattenRemotely = val2;
            }
        }

        public string BaseUri => _baseUri;
        public string BaseUriWithoutTrailing => _baseUri[0..^1];
        public string TokenSigningKey => _tokenSigningKey;
        public bool CanRegister => _canRegister;
        public string FileUploadPath => _fileUploadPath;
        public string FileUploadUrl => _fileUploadUrl;
        public bool RewriteRequestScheme => _rewriteRequestScheme;
        public bool UnflattenRemotely => _unflattenRemotely;

        public string BaseDomain => new Uri(BaseUri).Host;
        public string BasePath => new Uri(BaseUri).AbsolutePath;

        public static readonly List<Type> ServerToServerHandlers = new();
        public static readonly List<Type> ClientToServerHandlers = new();
        public static readonly List<IConverterFactory> Converters = new();
    }
}
