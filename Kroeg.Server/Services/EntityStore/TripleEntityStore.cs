using System.Threading.Tasks;
using Kroeg.Server.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using Kroeg.ActivityStreams;
using Newtonsoft.Json;

namespace Kroeg.Server.Services.EntityStore
{
    public class TripleEntityStore : IEntityStore
    {
        private readonly APContext _context;

        private static Dictionary<int, string> _attributeMapping = new Dictionary<int, string>();
        private static Dictionary<string, int> _inverseAttributeMapping = new Dictionary<string, int>();

        private void _commitCache()
        {
            foreach (var item in _reverseAttributeCache)
            {
                if (item.Value.AttributeId > 0)
                {
                    _attributeMapping[item.Value.AttributeId] = item.Value.Uri;
                    _inverseAttributeMapping[item.Value.Uri] = item.Value.AttributeId;
                }
            }
        }

        private Dictionary<string, TripleAttribute> _reverseAttributeCache = new Dictionary<string, TripleAttribute>();
        private static JsonLD.API _api = new JsonLD.API(null);

        public TripleEntityStore(APContext context)
        {
            _context = context;
        }

        public IEntityStore Bypass { get; set; }

        private async Task _preload(IEnumerable<int> ids)
        {
            var idset = new HashSet<int>(ids.Where(a => !_attributeMapping.ContainsKey(a)));
            
            var dbs = await _context.Attributes.Where(a => idset.Contains(a.AttributeId)).ToListAsync();
            foreach (var item in dbs)
            {
                _attributeMapping[item.AttributeId] = item.Uri;
                _inverseAttributeMapping[item.Uri] = item.AttributeId;
            }
        }

        private string _get(int id)
        {
            if (_attributeMapping.ContainsKey(id))
                return _attributeMapping[id];
            return null;
        }

        private async Task<int?> _reverseAttribute(string uri, bool rename)
        {
            if (_inverseAttributeMapping.ContainsKey(uri)) return _inverseAttributeMapping[uri];
            if (_reverseAttributeCache.ContainsKey(uri)) return _reverseAttributeCache[uri].AttributeId;

            var item = await _context.Attributes.FirstOrDefaultAsync(a => a.Uri == uri);

            if (item == null)
            {
                if (!rename) return null;

                item = new TripleAttribute { Uri = uri };
                _context.Attributes.Add(item);
            }

            if (item.AttributeId > 0)
            {
                _inverseAttributeMapping[uri] = item.AttributeId;
                _attributeMapping[item.AttributeId] = uri;
            }
            else
            {
                _reverseAttributeCache[uri] = item;
            }

            return item.AttributeId;
        }


        public async Task<APEntity> GetEntity(string id, bool doRemote)
        {
            var attr = await _reverseAttribute(id, false);
            APTripleEntity tripleEntity = null;
            if (attr != null)
                tripleEntity = await _context.TripleEntities.Include(a => a.Triples).FirstOrDefaultAsync(a => a.IdId == attr.Value);
            if (tripleEntity == null || (!tripleEntity.IsOwner && doRemote && id.StartsWith("http") && (DateTime.Now - tripleEntity.Updated).TotalDays > 7)) return null; // mini-cache
            if (tripleEntity == null) return null;

            return await _build(tripleEntity);
        }

        private async Task<APEntity> _build(APTripleEntity mold)
        {
            var triples = await _context.Triples.Where(a => a.SubjectEntityId == mold.EntityId).ToListAsync();

            await _preload(triples.Select(a => a.SubjectId)
                                .Concat(triples.Where(a => a.TypeId.HasValue).Select(a => a.TypeId.Value))
                                .Concat(triples.Where(a => a.AttributeId.HasValue).Select(a => a.AttributeId.Value)
                                .Concat(triples.Select(a => a.PredicateId))));
            var rdfType = await _reverseAttribute("rdf:type", true);

            var subjects = triples.GroupBy(a => a.SubjectId).ToDictionary(a => a.Key, a => a);
            Dictionary<int, ASObject> objects = subjects.ToDictionary(a => a.Key, a => new ASObject { Id = _attributeMapping[a.Key] });
            foreach (var obj in objects)
            {
                var result = obj.Value;

                if (result.Id.StartsWith("_:")) result.Id = null;

                result.Type.AddRange(subjects[obj.Key].Where(a => a.PredicateId == rdfType.Value).Select(a => _attributeMapping[a.AttributeId.Value]));

                foreach (var triple in subjects[obj.Key])
                {
                    if (triple.PredicateId == rdfType.Value) continue;

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
            _commitCache();

            return new APEntity { Data = mainObj, Id = mainObj.Id, Updated = mold.Updated, IsOwner = mold.IsOwner, Type = mold.Type };
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
            var data = entity.Data.Serialize(false, false);
            List<Triple> result = new List<Triple>();

            var triples = _api.MakeRDF(data)["@default"];

            foreach (var triple in triples)
            {
                var trans = new Triple();
                if (triple.Object.TypeIri == null)
                    trans.AttributeId = await _reverseAttribute(triple.Object.LexicalForm, true);
                else
                {
                    trans.Object = triple.Object.LexicalForm;
                    trans.TypeId = await _reverseAttribute(triple.Object.TypeIri, true);
                }

                trans.SubjectId = (await _reverseAttribute(triple.Subject, true)).Value;
                trans.PredicateId = (await _reverseAttribute(triple.Predicate, true)).Value;

                result.Add(trans);
            }
            
            return result;
        }

        public async Task<APEntity> StoreEntity(APEntity entity)
        {
            var attr = (await _reverseAttribute(entity.Id, true)).Value;

            var exists = await _context.TripleEntities.FirstOrDefaultAsync(a => a.IdId == attr);
            if (exists == null)
            {
                entity.Updated = DateTime.Now;
                var dbEntity = new APTripleEntity { Updated = entity.Updated, IsOwner = entity.IsOwner, Type = entity.Type };
                dbEntity.IdId = attr;

                var triples = await _newTriples(entity);
                foreach (var triple in triples)
                {
                    triple.SubjectEntity = dbEntity;
                    _context.Triples.Add(triple);
                }

                _context.TripleEntities.Add(dbEntity);
            }
            else
            {
                var triples = await _context.Triples.Where(a => a.PredicateId == exists.EntityId).GroupBy(a => a.TypeId).ToDictionaryAsync(a => a.Key, b => b);
                var compare = (await _newTriples(entity)).GroupBy(a => a.TypeId).ToDictionary(a => a.Key, b => b);

                var allKeys = new HashSet<int?>(triples.Keys.Concat(compare.Keys));
                foreach (var key in allKeys)
                {
                    if (compare.ContainsKey(key) && !triples.ContainsKey(key))
                    {
                        foreach (var triple in compare[key])
                        {
                            triple.PredicateId = exists.EntityId;
                            _context.Triples.Add(triple);
                        }
                    }
                    else if (!compare.ContainsKey(key) && triples.ContainsKey(key))
                    {
                        _context.Triples.RemoveRange(triples[key]);
                    }
                    else
                    {
                        var equal = triples[key].Where(a => compare[key].Any(b => b.Object ==a.Object && b.TypeId == a.TypeId && b.SubjectEntityId == a.SubjectEntityId)).ToList();
                        var removed = triples[key].Where(a => !compare[key].Any(b => b.Object ==a.Object && b.TypeId == a.TypeId && b.SubjectEntityId == a.SubjectEntityId)).ToList();
                        var added = compare[key].Where(a => !triples[key].Any(b => b.Object ==a.Object && b.TypeId == a.TypeId && b.SubjectEntityId == a.SubjectEntityId)).ToList();

                        _context.Triples.RemoveRange(removed);
                        foreach (var triple in added)
                        {
                            triple.PredicateId = exists.EntityId;
                            _context.Triples.Add(triple);
                        }
                    }
                }
            }

            return entity;
        }

        public async Task CommitChanges()
        {
            await _context.SaveChangesAsync();
        }
    }
}
