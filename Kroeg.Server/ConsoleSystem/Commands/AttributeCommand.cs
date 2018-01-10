using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Kroeg.Server.Models;
using Kroeg.EntityStore.Store;

namespace Kroeg.Server.ConsoleSystem.Commands
{
    public class AttributeCommand : IConsoleCommand
    {
        private readonly DbConnection _connection;

        public AttributeCommand(DbConnection connection)
        {
            _connection = connection;
        }

        public async Task Do(string[] args)
        {
            foreach (var item in args)
            {
                var attr = await _connection.QueryFirstOrDefaultAsync<TripleAttribute>("SELECT * from \"Attributes\" where \"Uri\" = @Uri", new { Uri = item });
                if (attr == null)
                    Console.WriteLine($"    -- {item} doesn't exist");
                else
                    Console.WriteLine($"    ID: {attr.AttributeId}, URI: {attr.Uri}");
            }
        }
    }
}