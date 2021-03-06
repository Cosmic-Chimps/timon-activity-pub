using System.Threading.Tasks;
using Kroeg.EntityStore.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using Kroeg.ActivityStreams;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using System.Data;
using Dapper;
using System.Data.Common;
using System.Diagnostics;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Dapr.Client;
using Kroeg.EntityStore.Messages;

namespace Kroeg.EntityStore.Store
{
    public class TripleEntityStore : IEntityStore
    {
        private readonly DbConnection _connection;
        private readonly DbConnection _attributeConnection;

        private readonly ILogger _logger;
        private readonly DaprClient _daprClient;

        private static Dictionary<int, string> _attributeMapping = new Dictionary<int, string>();
        private static Dictionary<string, int> _inverseAttributeMapping = new Dictionary<string, int>();

        private Dictionary<string, APEntity> _quickMap = new Dictionary<string, APEntity>();


        private static JsonLD.API _api = new JsonLD.API(null);

        public TripleEntityStore(DbConnection connection, ILogger<TripleEntityStore> logger, IConfiguration configuration, DaprClient daprClient)
        {
            var connectionString = configuration.GetValue<string>("CONNECTION_STRING") ?? configuration.GetConnectionString("Default");
            _connection = connection;
            _attributeConnection = new NpgsqlConnection(connectionString);
            _logger = logger;
            _daprClient = daprClient;
        }

        public IEntityStore Bypass { get; set; }

        private async Task _preload(IEnumerable<int> ids)
        {
            var idset = ids.Where(a => !_attributeMapping.ContainsKey(a)).ToList();
            if (idset.Count > 0)
            {
                var dbs = await _attributeConnection.QueryAsync<TripleAttribute>("select * from \"Attributes\" where \"AttributeId\" = any(@Ids)", new { Ids = idset });
                foreach (var item in dbs)
                {
                    _attributeMapping[item.AttributeId] = item.Uri;
                    lock (_inverseAttributeMapping)
                        _inverseAttributeMapping[item.Uri] = item.AttributeId;
                }
            }
        }

        public Dictionary<string, int> FindAttributes(List<string> attrs)
        {
            lock (_inverseAttributeMapping)
                return _inverseAttributeMapping.Where(a => attrs.Contains(a.Key)).ToDictionary(a => a.Key, a => a.Value);
        }

        private string _get(int id)
        {
            if (_attributeMapping.ContainsKey(id))
                return _attributeMapping[id];
            return null;
        }

        public async Task<int?> ReverseAttribute(string uri, bool create)
        {
            if (_inverseAttributeMapping.ContainsKey(uri))
            {
                return _inverseAttributeMapping[uri];
            }

            var item = await _attributeConnection.QueryFirstOrDefaultAsync<TripleAttribute>("select * from \"Attributes\" where \"Uri\" = @Uri", new { Uri = uri });

            if (item == null)
            {
                if (!create)
                {
                    return null;
                }

                item = new TripleAttribute { Uri = uri };
                item.AttributeId = await _attributeConnection.ExecuteScalarAsync<int>("insert into \"Attributes\" (\"Uri\") values (@Uri) on conflict (\"Uri\") do update set \"Uri\" = @Uri returning \"AttributeId\"", item);
            }

            _inverseAttributeMapping[uri] = item.AttributeId;
            _attributeMapping[item.AttributeId] = uri;
            return item.AttributeId;
        }

        public async Task<List<APEntity>> GetEntities(List<int> ids)
        {
            var allEntities = (await _connection.QueryAsync<APTripleEntity>("select * from \"TripleEntities\" where \"EntityId\" = any(@Ids)", new { Ids = ids })).OrderBy(a => ids.IndexOf(a.EntityId)).ToList();
            var allTriples = await _connection.QueryAsync<Triple>("select * from \"Triples\" where \"SubjectEntityId\" = any(@Ids)", new { Ids = ids });

            Dictionary<int, APEntity> results = new Dictionary<int, APEntity>();

            await _preload(allTriples.Select(a => a.SubjectId)
                                .Concat(allTriples.Where(a => a.TypeId.HasValue).Select(a => a.TypeId.Value))
                                .Concat(allTriples.Where(a => a.AttributeId.HasValue).Select(a => a.AttributeId.Value)
                                .Concat(allTriples.Select(a => a.PredicateId))));

            var rdfType = await ReverseAttribute("rdf:type", true);
            var rdfEnd = await ReverseAttribute("rdf:rest", true);

            foreach (var mold in allEntities)
            {
                results[mold.EntityId] = _buildRaw(mold, allTriples.Where(a => a.SubjectEntityId == mold.EntityId), rdfType.Value, rdfEnd.Value);
            }

            return ids.Select(a => results[a]).ToList();
        }


        public async Task<APEntity> GetEntity(string id, bool doRemote)
        {
            if (id == null)
            {
                return null;
            }
            // if (_quickMap.ContainsKey(id)) return _quickMap[id].Clone();

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var attr = await ReverseAttribute(id, false);
            APTripleEntity tripleEntity = null;
            if (attr != null)
                tripleEntity = await _connection.QueryFirstOrDefaultAsync<APTripleEntity>("select * from \"TripleEntities\" where \"IdId\" = @IdId limit 1", new { IdId = attr.Value });
            if (tripleEntity == null) return null;

            var b = await _build(tripleEntity);
            stopwatch.Stop();

            _logger.LogWarning("Getting ID {id} took {time}", b.Id, stopwatch.Elapsed);
            // _quickMap[id] = b;
            return b.Clone();
        }

        public async Task<APEntity> GetEntity(int id)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var entity = await _connection.QueryFirstOrDefaultAsync<APTripleEntity>("select * from \"TripleEntities\" where \"EntityId\" = @IdId limit 1", new { IdId = id });
            if (entity == null) return null;

            var b = await _build(entity);
            stopwatch.Stop();
            _logger.LogWarning("Getting ID {id} took {time}", b.Id, stopwatch.Elapsed);
            return b;
        }

        private APEntity _buildRaw(APTripleEntity mold, IEnumerable<Triple> triples, int rdfType, int rdfEnd)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var subjects = triples.GroupBy(a => a.SubjectId).ToDictionary(a => a.Key, a => a);
            Dictionary<int, ASObject> objects = subjects.ToDictionary(a => a.Key, a => new ASObject { Id = _attributeMapping[a.Key] });
            var listEnds = new HashSet<int>();
            var blankNodes = new Dictionary<int, Tuple<ASObject, string>>();
            var listParts = triples.Where(a => a.PredicateId == rdfEnd).ToDictionary(a => a.AttributeId.Value, a => a.SubjectId);

            foreach (var obj in objects)
            {
                var result = obj.Value;

                if (result.Id.StartsWith("_:")) result.Id = null;

                result.Type.AddRange(subjects[obj.Key].Where(a => a.PredicateId == rdfType).Select(a => _attributeMapping[a.AttributeId.Value]));

                foreach (var triple in subjects[obj.Key])
                {
                    if (triple.PredicateId == rdfType) continue;

                    var term = new ASTerm();
                    var predicateUrl = _attributeMapping[triple.PredicateId];

                    if (triple.AttributeId.HasValue && !listParts.ContainsValue(triple.AttributeId.Value) && objects.ContainsKey(triple.AttributeId.Value) && _attributeMapping[triple.AttributeId.Value].StartsWith("_:"))
                        term.SubObject = objects[triple.AttributeId.Value];
                    else
                    {
                        if (triple.TypeId.HasValue)
                            term.Type = _attributeMapping[triple.TypeId.Value];

                        if (triple.AttributeId.HasValue)
                            term.Id = _attributeMapping[triple.AttributeId.Value];

                        term.Primitive = _tripleToJson(triple.Object, term.Type);
                        if (_defaultTypes.Contains(term.Type))
                            term.Type = null;

                        if (predicateUrl == "rdf:rest")
                        {
                            if (term.Id == "rdf:nil")
                                listEnds.Add(triple.SubjectId);
                            listParts[triple.AttributeId.Value] = triple.SubjectId;
                        }

                        if (term.Id?.StartsWith("_:") == true)
                        {
                            blankNodes[triple.AttributeId.Value] = new Tuple<ASObject, string>(result, predicateUrl);
                        }
                    }


                    result[predicateUrl].Add(term);
                }
            }

            foreach (var listEnd in listEnds)
            {
                var listId = listEnd;
                var list = new List<ASTerm>();
                list.Add(objects[listId]["rdf:first"].First());
                while (listParts.ContainsKey(listId))
                {
                    listId = listParts[listId];
                    list.Add(objects[listId]["rdf:first"].First());
                }

                if (blankNodes.ContainsKey(listId))
                {
                    blankNodes[listId].Deconstruct(out var obj, out var name);

                    list.Reverse();

                    obj[name].Clear();
                    obj[name].AddRange(list);
                }
            }

            var mainObj = objects[mold.IdId];

            stopwatch.Stop();
            _logger.LogWarning("Molding {id} from cache took {time}", mainObj.Id, stopwatch.Elapsed);
            return new APEntity { Data = mainObj, Id = mainObj.Id, Updated = mold.Updated, IsOwner = mold.IsOwner, Type = mold.Type, DbId = mold.EntityId };
        }

        private async Task<APEntity> _build(APTripleEntity mold)
        {
            var triples = (await _connection.QueryAsync<Triple>("select * from \"Triples\" where \"SubjectEntityId\" = @Id", new { Id = mold.EntityId })).ToList();

            await _preload(triples.Select(a => a.SubjectId)
                                .Concat(triples.Where(a => a.TypeId.HasValue).Select(a => a.TypeId.Value))
                                .Concat(triples.Where(a => a.AttributeId.HasValue).Select(a => a.AttributeId.Value)
                                .Concat(triples.Select(a => a.PredicateId))));
            var rdfType = await ReverseAttribute("rdf:type", true);
            var rdfEnd = await ReverseAttribute("rdf:rest", true);

            var result = _buildRaw(mold, triples, rdfType.Value, rdfEnd.Value);

            return result;
        }

        private static HashSet<string> _defaultTypes = new HashSet<string>
        {
            "xsd:boolean", "xsd:double", "xsd:integer", "xsd:string", "rdf:langString"
        };

        private object _tripleToJson(string obj, string type)
        {
            try
            {
                if (type == "xsd:boolean")
                    return obj.ToLower() == "true";
                else if (type == "xsd:double")
                    return double.Parse(obj);
                else if (type == "xsd:integer" || type == "http://www.w3.org/2001/XMLSchema#nonNegativeInteger")
                    return int.Parse(obj);
                else
                    return obj;
            }
            catch (FormatException)
            {
                return obj;
            }
        }

        private async Task<List<Triple>> _newTriples(APEntity entity)
        {
            var data = entity.Data.Serialize(false, false);
            List<Triple> result = new List<Triple>();

            var triples = _api.MakeRDF(data)["@default"];

            foreach (var triple in triples)
            {
                var trans = new Triple();
                if (triple.Object.TypeIri == null)
                    trans.AttributeId = await ReverseAttribute(triple.Object.LexicalForm, true);
                else
                {
                    trans.Object = triple.Object.LexicalForm;
                    trans.TypeId = await ReverseAttribute(triple.Object.TypeIri, true);
                }

                trans.SubjectId = (await ReverseAttribute(triple.Subject, true)).Value;
                trans.PredicateId = (await ReverseAttribute(triple.Predicate, true)).Value;

                result.Add(trans);
            }

            return result;
        }

        public async Task<APEntity> StoreEntity(APEntity entity)
        {
            var idid = await ReverseAttribute(entity.Id, false);
            var exists = await _connection.QueryFirstOrDefaultAsync<APTripleEntity>("select * from \"TripleEntities\" where \"EntityId\" = @Id or \"IdId\" = @IdId limit 1", new { Id = entity.DbId, IdId = idid });

            if (exists == null)
            {
                entity.Updated = DateTime.Now;
                var dbEntity = new APTripleEntity { Updated = entity.Updated, IsOwner = entity.IsOwner, Type = entity.Type };
                var attr = (await ReverseAttribute(entity.Id, true)).Value;
                dbEntity.IdId = attr;
                dbEntity.EntityId = await _connection.ExecuteScalarAsync<int>("insert into \"TripleEntities\" (\"Updated\", \"IsOwner\", \"Type\", \"IdId\") values (@Updated, @IsOwner, @Type, @IdId) returning \"EntityId\"", dbEntity);

                var allTriples = await _newTriples(entity);
                foreach (var triple in allTriples)
                {
                    triple.SubjectEntityId = dbEntity.EntityId;
                }

                await _connection.ExecuteAsync("insert into \"Triples\" (\"SubjectId\", \"SubjectEntityId\", \"PredicateId\", \"AttributeId\", \"TypeId\", \"Object\") " +
                                                "values (@SubjectId, @SubjectEntityId, @PredicateId, @AttributeId, @TypeId, @Object)", allTriples);

                exists = dbEntity;
            }
            else
            {
                var triples = (await _connection.QueryAsync<Triple>("select * from \"Triples\" where \"SubjectEntityId\" = @SubjectEntityId", new { SubjectEntityId = exists.EntityId })).GroupBy(a => a.PredicateId).ToDictionary(a => a.Key, b => b);
                var compare = (await _newTriples(entity)).GroupBy(a => a.PredicateId).ToDictionary(a => a.Key, b => b);

                var allKeys = new HashSet<int>(triples.Keys.Concat(compare.Keys));
                foreach (var key in allKeys)
                {
                    if (compare.ContainsKey(key) && !triples.ContainsKey(key))
                    {
                        foreach (var triple in compare[key])
                        {
                            triple.SubjectEntityId = exists.EntityId;
                        }

                        await _connection.ExecuteAsync("insert into \"Triples\" (\"SubjectId\", \"SubjectEntityId\", \"PredicateId\", \"AttributeId\", \"TypeId\", \"Object\") values (@SubjectId, @SubjectEntityId, @PredicateId, @AttributeId, @TypeId, @Object)", compare[key]);
                    }
                    else if (!compare.ContainsKey(key) && triples.ContainsKey(key))
                    {
                        await _connection.ExecuteAsync("delete from \"Triples\" where \"TripleId\" = any(@Ids)", new { Ids = triples[key].Select(a => a.TripleId).ToList() });
                    }
                    else
                    {
                        await _connection.ExecuteAsync("delete from \"Triples\" where \"TripleId\" = any(@Ids)", new { Ids = triples[key].Select(a => a.TripleId).ToList() });

                        foreach (var triple in compare[key])
                        {
                            triple.SubjectEntityId = exists.EntityId;
                        }

                        await _connection.ExecuteAsync("insert into \"Triples\" (\"SubjectId\", \"SubjectEntityId\", \"PredicateId\", \"AttributeId\", \"TypeId\", \"Object\") values (@SubjectId, @SubjectEntityId, @PredicateId, @AttributeId, @TypeId, @Object)", compare[key]);
                    }
                }
            }

            entity.DbId = exists.EntityId;

            await PublishNoteToTimon(entity);
            await AcceptFollower(entity);

            return entity;
        }

        public async Task CommitChanges()
        {
            await Task.CompletedTask;
        }

        private async Task PublishNoteToTimon(APEntity entity)
        {
            if (entity.Type == "https://www.w3.org/ns/activitystreams#Note")
            {
                var content = entity.Data.FirstOrDefault(x => x.Key == "https://www.w3.org/ns/activitystreams#content");
                var attributedTo = entity.Data.FirstOrDefault(x => x.Key == "https://www.w3.org/ns/activitystreams#attributedTo");

                var text = content.Value.FirstOrDefault()?.Primitive as string;
                var userId = attributedTo.Value.FirstOrDefault()?.Id;

                if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(userId))
                {
                    return;
                }

                var message = new AddNoteToTimonMessage
                {
                    Content = text,
                    ActivityPubChannelId = userId
                };

                await _daprClient.PublishEventAsync<AddNoteToTimonMessage>(
                  "messagebus", "add-note-to-timon", message
                );
            }
        }

        private async Task AcceptFollower(APEntity entity)
        {
            //actor
            //to
            if (entity.Type == "https://www.w3.org/ns/activitystreams#Accept")
            {
                var actor = entity.Data.FirstOrDefault(x => x.Key == "https://www.w3.org/ns/activitystreams#actor");
                var to = entity.Data.FirstOrDefault(x => x.Key == "https://www.w3.org/ns/activitystreams#to");

                var activityPubChannelId = actor.Value.FirstOrDefault()?.Id as string; //channelid
                var follower = to.Value.FirstOrDefault()?.Id as string;

                if (string.IsNullOrEmpty(follower) || string.IsNullOrEmpty(activityPubChannelId))
                {
                    return;
                }

                var message = new UserFollowChannelMessage
                {
                    FollowerId = follower,
                    ActivityPubChannelId = activityPubChannelId
                };

                await _daprClient.PublishEventAsync<UserFollowChannelMessage>(
                  "messagebus", "user-follow-channel", message
                );
            }
        }
    }
}
