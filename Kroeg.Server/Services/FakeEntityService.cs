using System;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Salmon;
using Kroeg.Server.Tools;

namespace Kroeg.Server.Services
{
    public class FakeEntityService
    {
        private readonly APContext _context;
        private readonly EntityData _configuration;

        public FakeEntityService(APContext context, EntityData configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<ASObject> BuildFakeObject(APEntity entity, string fragment)
        {
            if (!_configuration.IsActor(entity.Data)) return null;

            if (fragment == "key")
            {
                var key = await _context.GetKey(entity.Id);
                var salm = new MagicKey(key.PrivateKey);
                var pemData = salm.AsPEM;

                var keyObj = new ASObject();
                keyObj.Replace("owner", ASTerm.MakeId(entity.Id));
                keyObj.Replace("publicKeyPem", ASTerm.MakePrimitive(pemData));
                keyObj.Id = $"{entity.Id}#key";
                return keyObj;
            }
            else if (fragment == "endpoints")
            {
                var data = entity.Data;
                var idu = new Uri(entity.Id);

                var basePath = $"{idu.Scheme}://{idu.Host}{(idu.IsDefaultPort?"":$":{idu.Port}")}{_configuration.BasePath}";

                var endpoints = new ASObject();
                endpoints.Replace("oauthAuthorizationEndpoint", ASTerm.MakeId(basePath + "auth/oauth?id=" + Uri.EscapeDataString(entity.Id)));
                endpoints.Replace("oauthTokenEndpoint", ASTerm.MakeId(basePath + "auth/token?"));
                endpoints.Replace("settingsEndpoint", ASTerm.MakeId(basePath + "settings/auth"));
                endpoints.Replace("uploadMedia", ASTerm.MakeId((string)data["outbox"].Single().Primitive));
                endpoints.Replace("relevantObjects", ASTerm.MakeId(basePath + "settings/relevant"));
                endpoints.Replace("proxyUrl", ASTerm.MakeId(basePath + "auth/proxy"));
                endpoints.Replace("jwks", ASTerm.MakeId(basePath + "auth/jwks?id=" + Uri.EscapeDataString(entity.Id)));
                endpoints.Replace("sharedInbox", ASTerm.MakeId(basePath + "/auth/sharedInbox"));
                endpoints.Id = entity.Id + "#endpoints";
                return endpoints;
            }

            return null;
        }
    }
}