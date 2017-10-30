using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
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
    }
}