using Npgsql;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dapper;
using Kroeg.Server.Middleware.Renderers;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.EntityStore.Store;
using Kroeg.Server.Tools;

namespace Kroeg.Server.ConsoleSystem.Commands
{
    public class CalculateVisibilityCommand : IConsoleCommand
    {
        private readonly TripleEntityStore _entityStore;
        private readonly NpgsqlConnection _connection;
        private readonly EntityFlattener _entityFlattener;
        private readonly IServiceProvider _provider;

        public CalculateVisibilityCommand(TripleEntityStore entityStore, NpgsqlConnection connection, EntityFlattener entityFlattener, IServiceProvider provider)
        {
            _entityStore = entityStore;
            _connection = connection;
            _provider = provider;
        }

        public async Task Do(string[] args)
        {
            foreach (var item in await _connection.QueryAsync<CollectionItem>("select * from \"CollectionItems\""))
            {
                var entity = await _entityStore.GetEntity(item.ElementId);
                var newState = DeliveryService.IsPublic(entity.Data) || entity.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Person");
                if (newState != item.IsPublic)
                    await _connection.ExecuteAsync("update \"CollectionItems\" set \"IsPublic\" = @IsPublic where \"CollectionItemId\" = @Id", new { IsPublic = newState, Id = item.CollectionItemId });
            }
        }
    }
}
