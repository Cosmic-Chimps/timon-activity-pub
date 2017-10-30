using System.Threading.Tasks;
using Kroeg.Server.Models;
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

namespace Kroeg.Server.Services.EntityStore
{
    public class TripleEntityStore : IEntityStore
    {
        private readonly DbConnection _connection;
        private readonly ILogger _logger;

        private static Dictionary<int, string> _attributeMapping = new Dictionary<int, string>();
        private static Dictionary<string, int> _inverseAttributeMapping = new Dictionary<string, int>();

        private Dictionary<string, APEntity> _quickMap = new Dictionary<string, APEntity>();


        private static JsonLD.API _api = new JsonLD.API(null);

        public TripleEntityStore(DbConnection connection, ILogger<TripleEntityStore> logger)
        {
            _connection = connection;
            _logger = logger;
        }

        public IEntityStore Bypass { get; set; }

        private async Task _preload(IEnumerable<int> ids)
        {
            var idset = ids.Where(a => !_attributeMapping.ContainsKey(a)).ToList();
            _logger.LogWarning("Preloading a list of {Count} attributes", idset.Count);
            if (idset.Count > 0)
            {
                var dbs = await _connection.QueryAsync<TripleAttribute>("select * from \"Attributes\" where \"AttributeId\" = any(@Ids)", new { Ids = idset });
                foreach (var item in dbs)
                {
                    _attributeMapping[item.AttributeId] = item.Uri;
                    _inverseAttributeMapping[item.Uri] = item.AttributeId;

                    _logger.LogInformation("Mapped {Id} to {Uri}", item.AttributeId, item.Uri);
                }
            }
        }

        private string _get(int id)
        {
            if (_attributeMapping.ContainsKey(id))
                return _attributeMapping[id];
            return null;
        }

        public async Task<int?> ReverseAttribute(string uri, bool create)
        {
            _logger.LogWarning("Trying to reverse-map {Uri}", uri);
            if (_inverseAttributeMapping.ContainsKey(uri))
            {
                _logger.LogInformation("{Uri} exists in cache.", uri);
                return _inverseAttributeMapping[uri];
            }

            var item = await _connection.QueryFirstOrDefaultAsync<TripleAttribute>("select * from \"Attributes\" where \"Uri\" = @Uri", new { Uri = uri });

            if (item == null)
            {
                if (!create)
                {
                    _logger.LogInformation("No item in database and not creating one");
                    return null;
                }

                item = new TripleAttribute { Uri = uri };
                item.AttributeId = await _connection.ExecuteScalarAsync<int>("insert into \"Attributes\" (\"Uri\") values (@Uri) returning \"AttributeId\"", item);
            }

            _inverseAttributeMapping[uri] = item.AttributeId;
            _attributeMapping[item.AttributeId] = uri;

            _logger.LogInformation("Mapped {Id} to {Uri}", item.AttributeId, item.Uri);
            return item.AttributeId;
        }

        public async Task<List<APEntity>> GetEntities(List<int> ids)
        {
            _logger.LogWarning("Getting a list of {Count} Entities based on ID", ids.Count);
            var allEntities = (await _connection.QueryAsync<APTripleEntity>("select * from \"TripleEntities\" where \"EntityId\" = any(@Ids)", new { Ids = ids })).OrderBy(a => ids.IndexOf(a.EntityId)).ToList();
            var allTriples = await _connection.QueryAsync<Triple>("select * from \"Triples\" where \"SubjectEntityId\" = any(@Ids)", new { Ids = ids });

            List<APEntity> results = new List<APEntity>();

            await _preload(allTriples.Select(a => a.SubjectId)
                                .Concat(allTriples.Where(a => a.TypeId.HasValue).Select(a => a.TypeId.Value))
                                .Concat(allTriples.Where(a => a.AttributeId.HasValue).Select(a => a.AttributeId.Value)
                                .Concat(allTriples.Select(a => a.PredicateId))));

            var rdfType = await ReverseAttribute("rdf:type", true);

            foreach (var mold in allEntities)
            {
                _logger.LogInformation("Building an APEntity for {EntityId}", mold.EntityId);
                results.Add(_buildRaw(mold, allTriples.Where(a => a.SubjectEntityId == mold.EntityId), rdfType.Value));
            }

            return results;
        }


        public async Task<APEntity> GetEntity(string id, bool doRemote)
        { 
            _logger.LogWarning("Getting one entity based on URI {id}", id);

            var attr = await ReverseAttribute(id, false);
            APTripleEntity tripleEntity = null;
            if (attr != null)
                tripleEntity = await _connection.QueryFirstOrDefaultAsync<APTripleEntity>("select * from \"TripleEntities\" where \"IdId\" = @IdId limit 1", new { IdId = attr.Value });
            if (tripleEntity == null || (!tripleEntity.IsOwner && doRemote && id.StartsWith("http") && (DateTime.Now - tripleEntity.Updated).TotalDays > 7)) return null; // mini-cache
            if (tripleEntity == null) return null;

            var b = await _build(tripleEntity);
            return b;
        }

        public async Task<APEntity> GetEntity(int id)
        {
            _logger.LogWarning("Getting one entity based on ID {id}", id);

            var entity = await _connection.QueryFirstOrDefaultAsync<APTripleEntity>("select * from \"TripleEntities\" where \"EntityId\" = @Id limit 1", new { IdId = id });
            if (entity == null) return null;

            var b = await _build(entity);
            return b;
        }

        private APEntity _buildRaw(APTripleEntity mold, IEnumerable<Triple> triples, int rdfType)
        {
            var subjects = triples.GroupBy(a => a.SubjectId).ToDictionary(a => a.Key, a => a);
            Dictionary<int, ASObject> objects = subjects.ToDictionary(a => a.Key, a => new ASObject { Id = _attributeMapping[a.Key] });

            _logger.LogWarning("Building an APEntity with {Count} objects", objects.Count);
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

                    if (triple.TypeId.HasValue)
                        term.Type = _attributeMapping[triple.TypeId.Value];

                    if (triple.AttributeId.HasValue)
                        term.Id = _attributeMapping[triple.AttributeId.Value];

                    term.Primitive = _tripleToJson(triple.Object, term.Type);
                    if (_defaultTypes.Contains(term.Type))
                        term.Type = null;


                    result[predicateUrl].Add(term);
                }
            }

            var mainObj = objects[mold.IdId];

            return new APEntity { Data = mainObj, Id = mainObj.Id, Updated = mold.Updated, IsOwner = mold.IsOwner, Type = mold.Type, DbId = mold.EntityId };
        }

        private async Task<APEntity> _build(APTripleEntity mold)
        {
            var triples = (await _connection.QueryAsync<Triple>("select * from \"Triples\" where \"SubjectEntityId\" = @Id", new { Id = mold.EntityId })).ToList();

            _logger.LogWarning("Preloading attribute IDs for {Count} triples", triples.Count);
            await _preload(triples.Select(a => a.SubjectId)
                                .Concat(triples.Where(a => a.TypeId.HasValue).Select(a => a.TypeId.Value))
                                .Concat(triples.Where(a => a.AttributeId.HasValue).Select(a => a.AttributeId.Value)
                                .Concat(triples.Select(a => a.PredicateId))));
            var rdfType = await ReverseAttribute("rdf:type", true);

            var result = _buildRaw(mold, triples, rdfType.Value);

            return result;
        }

        private static HashSet<string> _defaultTypes = new HashSet<string>
        {
            "xsd:boolean", "xsd:double", "xsd:integer", "xsd:string", "rdf:langString"
        };

        private object _tripleToJson(string obj, string type)
        {
            if (type == "xsd:boolean")
                return obj == "true";
            else if (type == "xsd:double")
                return double.Parse(obj);
            else if (type == "xsd:integer")
                return int.Parse(type);
            else
                return obj;
        }

        private async Task<List<Triple>> _newTriples(APEntity entity)
        {
            _logger.LogWarning("Building list of triples for entity {Id}", entity.Id);
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
            var exists = await _connection.QueryFirstOrDefaultAsync<APTripleEntity>("select * from \"TripleEntities\" where \"EntityId\" = @Id limit 1", new { Id = entity.DbId });
            
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
                        var removed = triples[key].Where(a => !compare[key].Any(b => b.Object == a.Object && b.TypeId == a.TypeId && b.SubjectId == a.SubjectId)).ToList();
                        var added = compare[key].Where(a => !triples[key].Any(b => b.Object == a.Object && b.TypeId == a.TypeId && b.SubjectId == a.SubjectId)).ToList();

                        await _connection.ExecuteAsync("delete from \"Triples\" where \"TripleId\" = any(@Ids)", new { Ids = removed.Select(a => a.TripleId).ToList() });
                        foreach (var triple in added)
                        {
                            triple.SubjectEntityId = exists.EntityId;
                        }

                        await _connection.ExecuteAsync("insert into \"Triples\" (\"SubjectId\", \"SubjectEntityId\", \"PredicateId\", \"AttributeId\", \"TypeId\", \"Object\") values (@SubjectId, @SubjectEntityId, @PredicateId, @AttributeId, @TypeId, @Object)", added);
                    }
                }
            }

            entity.DbId = exists.EntityId;

            return entity;
        }

        public async Task CommitChanges()
        {
        }
    }
}
