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

        private Dictionary<string, APEntity> _quickMap = new Dictionary<string, APEntity>();


        private static JsonLD.API _api = new JsonLD.API(null);

        public TripleEntityStore(APContext context)
        {
            _context = context;
        }

        public IEntityStore Bypass { get; set; }

        private async Task _preload(IEnumerable<int> ids)
        {
            Console.WriteLine("___ start _preload");
            var idset = new HashSet<int>(ids.Where(a => !_attributeMapping.ContainsKey(a)));
            if (idset.Count > 0)
            {
                var dbs = await _context.Attributes.Where(a => idset.Contains(a.AttributeId)).ToListAsync();
                foreach (var item in dbs)
                {
                    _attributeMapping[item.AttributeId] = item.Uri;
                    _inverseAttributeMapping[item.Uri] = item.AttributeId;
                }
            }
            
            Console.WriteLine("___ end _preload");

        }

        private string _get(int id)
        {
            if (_attributeMapping.ContainsKey(id))
                return _attributeMapping[id];
            return null;
        }

        public async Task<int?> ReverseAttribute(string uri, bool create)
        {
            Console.WriteLine("___ start ReverseAttribute");
            if (_inverseAttributeMapping.ContainsKey(uri))
            {
                Console.WriteLine("___ end ReverseAttribute");
                return _inverseAttributeMapping[uri];
            }

            var item = await _context.Attributes.FirstOrDefaultAsync(a => a.Uri == uri);

            if (item == null)
            {
                if (!create)
                {
                    Console.WriteLine("___ end ReverseAttribute");
                    return null;
                }

                item = new TripleAttribute { Uri = uri };
                _context.Attributes.Add(item);
                await _context.SaveChangesAsync();
            }

            _inverseAttributeMapping[uri] = item.AttributeId;
            _attributeMapping[item.AttributeId] = uri;

            Console.WriteLine("___ end ReverseAttribute");
            return item.AttributeId;
        }

        public async Task<List<APEntity>> GetEntities(List<int> ids)
        {
            Console.WriteLine("___ start GetEntities");
            
            var allEntities = await _context.TripleEntities.Where(a => ids.Contains(a.EntityId)).OrderBy(a => ids.IndexOf(a.EntityId)).ToListAsync();
            var allTriples = await _context.Triples.Where(a => ids.Contains(a.SubjectEntityId)).ToListAsync();

            List<APEntity> results = new List<APEntity>();

            await _preload(allTriples.Select(a => a.SubjectId)
                                .Concat(allTriples.Where(a => a.TypeId.HasValue).Select(a => a.TypeId.Value))
                                .Concat(allTriples.Where(a => a.AttributeId.HasValue).Select(a => a.AttributeId.Value)
                                .Concat(allTriples.Select(a => a.PredicateId))));

            var rdfType = await ReverseAttribute("rdf:type", true);

            foreach (var mold in allEntities)
            {
                results.Add(_buildRaw(mold, allTriples.Where(a => a.SubjectEntityId == mold.EntityId), rdfType.Value));
            }

            Console.WriteLine("___ end GetEntities");
            return results;
        }


        public async Task<APEntity> GetEntity(string id, bool doRemote)
        {
            Console.WriteLine("___ start GetEntity");

            var attr = await ReverseAttribute(id, false);
            APTripleEntity tripleEntity = null;
            if (attr != null)
                tripleEntity = await _context.TripleEntities.FirstOrDefaultAsync(a => a.IdId == attr.Value);
            if (tripleEntity == null || (!tripleEntity.IsOwner && doRemote && id.StartsWith("http") && (DateTime.Now - tripleEntity.Updated).TotalDays > 7)) return null; // mini-cache
            if (tripleEntity == null) return null;

            var b = await _build(tripleEntity);
            Console.WriteLine("___ end GetEntity");
            return b;
        }

        public async Task<APEntity> GetEntity(int id)
        {
            Console.WriteLine("___ start GetEntity");

            var entity = await _context.TripleEntities.FirstOrDefaultAsync(a => a.EntityId == id);
            if (entity == null) return null;

            var b = await _build(entity);
            Console.WriteLine("___ end GetEntity");
            return b;
        }

        private APEntity _buildRaw(APTripleEntity mold, IEnumerable<Triple> triples, int rdfType)
        {
            var subjects = triples.GroupBy(a => a.SubjectId).ToDictionary(a => a.Key, a => a);
            Dictionary<int, ASObject> objects = subjects.ToDictionary(a => a.Key, a => new ASObject { Id = _attributeMapping[a.Key] });
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
            var triples = await _context.Triples.Where(a => a.SubjectEntityId == mold.EntityId).ToListAsync();

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
            var exists = await _context.TripleEntities.FindAsync(entity.DbId);
            
            if (exists == null)
            {
                entity.Updated = DateTime.Now;
                var dbEntity = new APTripleEntity { Updated = entity.Updated, IsOwner = entity.IsOwner, Type = entity.Type };
                var attr = (await ReverseAttribute(entity.Id, true)).Value;
                dbEntity.IdId = attr;
                _context.TripleEntities.Add(dbEntity);

                var allTriples = await _newTriples(entity);
                foreach (var triple in allTriples)
                {
                    triple.SubjectEntityId = dbEntity.EntityId;
                    _context.Triples.Add(triple);
                }

                exists = dbEntity;
            }
            else
            {
                var triples = await _context.Triples.Where(a => a.SubjectEntityId == exists.EntityId).GroupBy(a => a.PredicateId).ToDictionaryAsync(a => a.Key, b => b);
                var compare = (await _newTriples(entity)).GroupBy(a => a.PredicateId).ToDictionary(a => a.Key, b => b);

                var allKeys = new HashSet<int>(triples.Keys.Concat(compare.Keys));
                foreach (var key in allKeys)
                {
                    if (compare.ContainsKey(key) && !triples.ContainsKey(key))
                    {
                        foreach (var triple in compare[key])
                        {
                            triple.SubjectEntityId = exists.EntityId;
                            _context.Triples.Add(triple);
                        }
                    }
                    else if (!compare.ContainsKey(key) && triples.ContainsKey(key))
                    {
                        _context.Triples.RemoveRange(triples[key]);
                    }
                    else
                    {
                        var removed = triples[key].Where(a => !compare[key].Any(b => b.Object == a.Object && b.TypeId == a.TypeId && b.SubjectId == a.SubjectId)).ToList();
                        var added = compare[key].Where(a => !triples[key].Any(b => b.Object == a.Object && b.TypeId == a.TypeId && b.SubjectId == a.SubjectId)).ToList();

                        _context.Triples.RemoveRange(removed);
                        foreach (var triple in added)
                        {
                            triple.SubjectEntityId = exists.EntityId;
                            _context.Triples.Add(triple);
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();

            entity.DbId = exists.EntityId;

            return entity;
        }

        public async Task CommitChanges()
        {
        }
    }
}
