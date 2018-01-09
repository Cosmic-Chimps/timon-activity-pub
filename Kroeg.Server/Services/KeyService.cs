using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using System.Net.Http;
using Kroeg.Server.Salmon;
using System.Text;
using System;
using Dapper;
using Kroeg.Server.Models;

namespace Kroeg.Server.Services
{
    public class KeyService
    {
        private readonly DbConnection _connection;

        public KeyService(DbConnection connection)
        {
            _connection = connection;
        }

        public async Task<SalmonKey> GetKey(string entityId)
        {
            var inverse = await _connection.QuerySingleOrDefaultAsync<TripleAttribute>("select * from \"Attributes\" where \"Uri\" = @Uri", new { Uri = entityId });
            if (inverse == null) return null;

            var entity = await _connection.QuerySingleOrDefaultAsync<APTripleEntity>("select * from \"TripleEntities\" where \"IdId\" = @Id", new { Id = inverse.AttributeId });
            if (entity == null) return null;

            var res = await _connection.QuerySingleOrDefaultAsync<SalmonKey>("select * from \"SalmonKeys\" where \"EntityId\" = @Id", new { Id = entity.EntityId });
            if (res != null) return res;

            res = new SalmonKey()
            {
                EntityId = entity.EntityId,
                PrivateKey = Salmon.MagicKey.Generate().PrivateKey
            };

            await _connection.ExecuteAsync("insert into \"SalmonKeys\" (\"EntityId\", \"PrivateKey\") values (@EntityId, @PrivateKey)", res);
            return res;
        }

        public async Task<string> BuildHTTPSignature(string ownerId, HttpRequestMessage message)
        {
            string[] headers = new string[] { "(request-target)", "date", "authorization", "content-type" };
            var toSign = new StringBuilder();
            foreach (var header in headers)
            {
                if (header == "(request-target)")
                    toSign.Append($"{header}: {message.Method.Method.ToLower()} {message.RequestUri.PathAndQuery}\n");
                else
                {
                    if (message.Headers.TryGetValues(header, out var vals))
                        toSign.Append($"{header}: {string.Join(", ", vals)}\n");
                    else if (message.Content != null && message.Content.Headers.TryGetValues(header, out var cvals))
                        toSign.Append($"{header}: {string.Join(", ", cvals)}\n");
                    else
                        toSign.Append($"{header}: \n");
                }
            }

            toSign.Remove(toSign.Length - 1, 1);

            var key = await GetKey(ownerId);
            var magic = new MagicKey(key.PrivateKey);
            var signed = Convert.ToBase64String(magic.Sign(Encoding.UTF8.GetBytes(toSign.ToString())));

            var ownerOrigin = new Uri(ownerId);
            var keyId = ownerId + "#key";

            return $"keyId=\"{keyId}\",algorithm=\"rsa-sha256\",headers=\"{string.Join(" ", headers)}\",signature=\"{signed}\"";
        }
    }
}
