using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.ConsoleSystem.Commands
{
    public class TriplesCommand : IConsoleCommand
    {
        private readonly DbConnection _connection;

        public TriplesCommand(DbConnection connection)
        {
            _connection = connection;
        }

        private async Task<Tuple<APTripleEntity, List<Triple>, Dictionary<int, TripleAttribute>>> _getEntity(string id)
        {
            var trid = await _connection.QueryFirstOrDefaultAsync<TripleAttribute>("SELECT * from \"Attributes\" where \"Uri\" = @Uri", new { Uri = id });
            if (trid == null) return null;

            var entity = await _connection.QueryFirstOrDefaultAsync<APTripleEntity>("SELECT * from \"TripleEntities\" where \"IdId\" = @IdId", new { IdId = trid.AttributeId });
            var triples = (await _connection.QueryAsync<Triple>("SELECT * from \"Triples\" where \"SubjectEntityId\" = @Id", new { Id = entity.EntityId })).ToList();
            var attrIds = triples.Select(a => a.SubjectId)
                                .Concat(triples.Where(a => a.TypeId.HasValue).Select(a => a.TypeId.Value))
                                .Concat(triples.Where(a => a.AttributeId.HasValue).Select(a => a.AttributeId.Value)
                                .Concat(triples.Select(a => a.PredicateId)));
            var attributes = await _connection.QueryAsync<TripleAttribute>("SELECT * from \"Attributes\" where \"AttributeId\" = any(@Ids)", new { Ids = attrIds.ToList() });

            return new Tuple<APTripleEntity, List<Triple>, Dictionary<int, TripleAttribute>>(entity, triples, attributes.ToDictionary(a => a.AttributeId, b => b));
        }

        public async Task Do(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                var item = args[i];
                var data = await _getEntity(item);
                var mode = "r";
                var modes = new List<string>();
                if ((i + 1) < args.Length && (args[i + 1] == "+" || args[i + 1] == "++" || args[i + 1] == "-"))
                {
                    mode = args[i + 1];
                    modes = args.Skip(i + 2).Take(1 + (mode != "-" ? 1 : 0) + (mode == "++" ? 1 : 0)).ToList();
                    i += modes.Count + 1;
                }

                Console.WriteLine($"{mode} ! {String.Join("! ", modes)}");

                if (data == null)
                {
                    Console.WriteLine($"could not find {item}");
                }

                if (mode == "r")
                {
                    Console.WriteLine($"-- {item}");
                    foreach (var triple in data.Item2)
                    {
                        Console.Write($"   {triple.TripleId} <{data.Item3[triple.SubjectId].Uri}> <{data.Item3[triple.PredicateId].Uri}> ");
                        if (triple.AttributeId.HasValue)
                        {
                            Console.WriteLine($"<{data.Item3[triple.AttributeId.Value].Uri}> .");
                        }
                        else if (triple.TypeId.HasValue)
                        {
                            Console.WriteLine($"\"{triple.Object}\"^^{data.Item3[triple.TypeId.Value].Uri} .");
                        }
                        else
                        {
                            Console.WriteLine($"???");
                        }
                    }
                }
                else if (mode == "+")
                {
                    var triple = new Triple {
                        SubjectEntityId = data.Item1.EntityId,
                        SubjectId = data.Item1.IdId,
                        PredicateId = (await _connection.QueryFirstAsync<TripleAttribute>("SELECT * from \"Attributes\" where \"Uri\" = @Uri", new { Uri = modes[0] })).AttributeId,
                        AttributeId = (await _connection.QueryFirstOrDefaultAsync<TripleAttribute>("SELECT * from \"Attributes\" where \"Uri\" = @Uri", new { Uri = modes[1] }))?.AttributeId
                    };

                    if (triple.AttributeId == null)
                    {
                        triple.Object = modes[1];
                        triple.TypeId = (await _connection.QueryFirstAsync<TripleAttribute>("SELECT * from \"Attributes\" where \"Uri\" = 'xsd:string'")).AttributeId;
                    }

                    await _connection.ExecuteAsync("INSERT INTO \"Triples\" (\"SubjectEntityId\", \"SubjectId\", \"PredicateId\", \"AttributeId\", \"Object\", \"TypeId\") VALUES (@SubjectEntityId, @SubjectId, @PredicateId, @AttributeId, @Object, @TypeId)", triple);
                }
                else if (mode == "++")
                {
                    var triple = new Triple {
                        SubjectEntityId = data.Item1.EntityId,
                        SubjectId = (await _connection.QueryFirstAsync<TripleAttribute>("SELECT * from \"Attributes\" where \"Uri\" = @Uri", new { Uri = modes[0] })).AttributeId,
                        PredicateId = (await _connection.QueryFirstAsync<TripleAttribute>("SELECT * from \"Attributes\" where \"Uri\" = @Uri", new { Uri = modes[1] })).AttributeId,
                        AttributeId = (await _connection.QueryFirstOrDefaultAsync<TripleAttribute>("SELECT * from \"Attributes\" where \"Uri\" = @Uri", new { Uri = modes[2] }))?.AttributeId
                    };

                    if (triple.AttributeId == null)
                    {
                        triple.Object = modes[2];
                        triple.TypeId = (await _connection.QueryFirstOrDefaultAsync<TripleAttribute>("SELECT * from \"Attributes\" where \"Uri\" = 'xsd:string'")).AttributeId;
                    }

                    await _connection.ExecuteAsync("INSERT INTO \"Triples\" (\"SubjectEntityId\", \"SubjectId\", \"PredicateId\", \"AttributeId\", \"Object\", \"TypeId\") VALUES (@SubjectEntityId, @SubjectId, @PredicateId, @AttributeId, @Object, @TypeId)", triple);
                }
                else if (mode == "-")
                {
                    await _connection.ExecuteAsync("DELETE FROM \"Triples\" where \"TripleId\" = @Id", new { Id = int.Parse(modes[0]) });
                }
            }
        }
    }
}