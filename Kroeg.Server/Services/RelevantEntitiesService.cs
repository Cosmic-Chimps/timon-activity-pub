using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.Server.Services.EntityStore;
using System.Data;
using System.Data.Common;
using Dapper;
using System.Collections;

namespace Kroeg.Server.Services
{
    public class RelevantEntitiesService
    {
        private readonly DbConnection _connection;
        private readonly TripleEntityStore _entityStore;

        public interface IQueryStatement
        {
            IEnumerable<string> RequiredProperties { get; }

            string BuildSQL(Dictionary<string, int> map);
        }

        public class ContainsAnyStatement : IQueryStatement, IEnumerable<string>
        {
            public string Predicate { get; set; }
            public List<string> Values { get; set; } = new List<string>();

            public ContainsAnyStatement(string predicate)
            {
                Predicate = predicate;
            }

            public void Add(string val) => Values.Add(val);

            public IEnumerable<string> RequiredProperties => Values.Concat(new string[] { Predicate });

            public string BuildSQL(Dictionary<string, int> map) =>
                $"exists(select 1 from \"Triples\" where \"PredicateId\" = {map[Predicate]} and \"AttributeId\" in ({string.Join(",", Values.Select(a => map[a].ToString()))}) and \"SubjectId\" = a.\"IdId\" and \"SubjectEntityId\" = a.\"EntityId\")";

            IEnumerator<string> IEnumerable<string>.GetEnumerator() => Values.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => Values.GetEnumerator();
        }

        public class AllStatement : IQueryStatement, IEnumerable<IQueryStatement>
        {
            public List<IQueryStatement> Statements { get; } = new List<IQueryStatement>();

            public void Add(IQueryStatement statement) => Statements.Add(statement);

            public IEnumerable<string> RequiredProperties => Statements.SelectMany(a => a.RequiredProperties);
            public string BuildSQL(Dictionary<string, int> map) => "(" + string.Join(" and ", Statements.Select(a => a.BuildSQL(map))) + ")";

            IEnumerator<IQueryStatement> IEnumerable<IQueryStatement>.GetEnumerator() => Statements.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => Statements.GetEnumerator();
        }

        public class AnyStatement : IQueryStatement, IEnumerable<IQueryStatement>
        {
            public List<IQueryStatement> Statements { get; } = new List<IQueryStatement>();

            public void Add(IQueryStatement statement) => Statements.Add(statement);

            public IEnumerable<string> RequiredProperties => Statements.SelectMany(a => a.RequiredProperties);
            public string BuildSQL(Dictionary<string, int> map) => "(" + string.Join(" or ", Statements.Select(a => a.BuildSQL(map))) + ")";

            IEnumerator<IQueryStatement> IEnumerable<IQueryStatement>.GetEnumerator() => Statements.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => Statements.GetEnumerator();
        }

        public RelevantEntitiesService(TripleEntityStore entityStore, DbConnection connection)
        {
            _connection = connection;
            _entityStore = entityStore;
        }

        private class RelevantObjectJson
        {
            [JsonProperty("type")]
            public string Type { get; set; }
            [JsonProperty("object")]
            public string Object { get; set; }
            [JsonProperty("actor")]
            public string Actor { get; set; }
        }

        private class PreferredUsernameJson
        {
            [JsonProperty("preferredUsername")]
            public string PreferredUsername { get; set; }
        }

        private class FollowerJson
        {
            [JsonProperty("followers")]
            public string Followers { get; set; }
        }

        private async Task<List<APEntity>> _search(Dictionary<string, string> lookFor, int? inCollectionId = null)
        {
            var attributeMapping = new Dictionary<int, int>();
            foreach (var val in lookFor)
            {
                var reverseId = await _entityStore.ReverseAttribute(val.Key, false);
                if (reverseId == null) return new List<APEntity>();

                var reverseVal = await _entityStore.ReverseAttribute(val.Value, false);
                if (reverseVal == null) return new List<APEntity>();

                attributeMapping[reverseId.Value] = reverseVal.Value;
            }

            var start = $"select a.* from \"TripleEntities\" a where ";
            start += string.Join(" and ", attributeMapping.Select(a => $"exists(select 1 from \"Triples\" where \"PredicateId\" = {a.Key} and \"AttributeId\" = {a.Value} and \"SubjectId\" = a.\"IdId\" and \"SubjectEntityId\" = a.\"EntityId\")"));

            if (inCollectionId != null)
                start += $" and exists(select 1 from \"CollectionItems\" where \"CollectionId\" = {inCollectionId} and \"CollectionItemId\" = a.\"EntityId\")";

            return await _entityStore.GetEntities((await _connection.QueryAsync<APTripleEntity>(start)).Select(a => a.EntityId).ToList());
        }

        public async Task<List<APEntity>> Query(IQueryStatement statement, int maxId = int.MaxValue, int minId = int.MinValue, int count = 0, int? inCollectionId = null)
        {
            var attributeMapping = new Dictionary<string, int>();
            foreach (var val in statement.RequiredProperties)
            {
                var id = await _entityStore.ReverseAttribute(val, true);

                attributeMapping[val] = id.Value;
            }

            var start = $"select a.* from \"TripleEntities\" a where a.\"EntityId\" > @MinId and a.\"EntityId\" < @MaxId and ";
            start += statement.BuildSQL(attributeMapping);

            if (inCollectionId != null)
                start += $" and exists(select 1 from \"CollectionItems\" where \"CollectionId\" = {inCollectionId} and \"CollectionItemId\" = a.\"EntityId\")";

            start += " order by a.\"EntityId\" desc ";

            if (count > 0)
                start += " limit " + count;

            return await _entityStore.GetEntities((await _connection.QueryAsync<APTripleEntity>(start, new { MinId = minId, MaxId = maxId })).Select(a => a.EntityId).ToList());
        }

        public async Task FindTransparentPredicates(Dictionary<string, APEntity> objects, string actor)
        {
            var allObjectIds = _entityStore.FindAttributes(objects.Values.Select(a => a.Id).ToList()).Values.ToList();
            var actorId = (await _entityStore.ReverseAttribute(actor, true)).Value;
            var objectAttr = (await _entityStore.ReverseAttribute("https://www.w3.org/ns/activitystreams#object", true)).Value;
            var actorAttr = (await _entityStore.ReverseAttribute("https://www.w3.org/ns/activitystreams#actor", true)).Value;
            var typeAttr = (await _entityStore.ReverseAttribute("rdf:type", true)).Value;
            var undoAttr = (await _entityStore.ReverseAttribute("https://www.w3.org/ns/activitystreams#Undo", true)).Value;
            
            var query = "select a.* from \"TripleEntities\" a where a.\"IsOwner\" = TRUE" +
                $" and exists(select 1 from \"Triples\" where \"PredicateId\" = @ActorAttr and \"AttributeId\" = @ActorId and \"SubjectId\" = a.\"IdId\" and \"SubjectEntityId\" = a.\"EntityId\")" +
                $" and exists(select 1 from \"Triples\" where \"PredicateId\" = @ObjectAttr and \"AttributeId\" = any(@Ids) and \"SubjectId\" = a.\"IdId\" and \"SubjectEntityId\" = a.\"EntityId\")";

            var entityShapes = await _connection.QueryAsync<APTripleEntity>(query,
                new {
                    ActorAttr = actorAttr,
                    ActorId = actorId,
                    ObjectAttr = objectAttr,
                    Ids = allObjectIds
                });

            var undoneShapes = await _connection.QueryAsync<APTripleEntity>(query,
                new {
                    ActorAttr = typeAttr,
                    ActorId = undoAttr,
                    ObjectAttr = objectAttr,
                    Ids = entityShapes.Select(a => a.IdId).ToList()
                });
            var properEntities = await _entityStore.GetEntities(entityShapes.Concat(undoneShapes).Select(a => a.EntityId).ToList());
            var undoneObjects = properEntities.Where(a => a.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Undo")).Select(a => a.Data["object"].First().Id).ToList();

            var intactObjects = properEntities.Where(a => !undoneObjects.Contains(a.Id)).ToList();

            foreach (var obj in intactObjects)
            {
                if (!objects.ContainsKey(obj.Data["object"].First().Id)) continue;

                var target = objects[obj.Data["object"].First().Id];
                if (obj.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Like"))
                    target.Data["c2s:likes"].Add(ASTerm.MakeSubObject(obj.Data));
                if (obj.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Follow"))
                    target.Data["c2s:follows"].Add(ASTerm.MakeSubObject(obj.Data));
                if (obj.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Announce"))
                    target.Data["c2s:announces"].Add(ASTerm.MakeSubObject(obj.Data));
                if (obj.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Accept"))
                    target.Data["c2s:accepts"].Add(ASTerm.MakeSubObject(obj.Data));
                if (obj.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Reject"))
                    target.Data["c2s:rejects"].Add(ASTerm.MakeSubObject(obj.Data));
            }
        }

        private async Task<List<APEntity>> _searchLike(Dictionary<string, string> lookFor, string likeId, string likeValue, bool? isOwner = null)
        {
            var attributeMapping = new Dictionary<int, int>();
            foreach (var val in lookFor)
            {
                if (val.Value == null) continue;

                var reverseId = await _entityStore.ReverseAttribute(val.Key, false);
                if (reverseId == null) return new List<APEntity>();

                var reverseVal = await _entityStore.ReverseAttribute(val.Value, false);
                if (reverseVal == null) return new List<APEntity>();

                attributeMapping[reverseId.Value] = reverseVal.Value;
            }

            var likeAttrId = await _entityStore.ReverseAttribute(likeId, false);
            if (likeAttrId == null) return new List<APEntity>();

            var start = $"select a.* from \"TripleEntities\" a where exists(select 1 from \"Triples\" where \"PredicateId\" = {likeAttrId.Value} and \"Object\" like @Like and \"SubjectId\" = a.\"IdId\" and \"SubjectEntityId\" = a.\"EntityId\") ";
            if (lookFor.Count > 0) start += "and ";
            start += string.Join(" and ", attributeMapping.Select(a => $"exists(select 1 from \"Triples\" where \"PredicateId\" = {a.Key} and \"AttributeId\" = {a.Value} and \"SubjectId\" = a.\"IdId\" and \"SubjectEntityId\" = a.\"EntityId\")"));

            if (isOwner.HasValue)
                start += " and a.\"IsOwner\" = " + (isOwner.Value ? "TRUE" : "FALSE");

            return await _entityStore.GetEntities((await _connection.QueryAsync<APTripleEntity>(start, new { Like = likeValue })).Select(a => a.EntityId).ToList());
        }

        public async Task<List<APEntity>> FindRelevantObject(string authorId, string objectType, string objectId, APEntity inCollection = null)
        {
            return await _search(new Dictionary<string, string> {
                ["rdf:type"] = objectType,
                ["https://www.w3.org/ns/activitystreams#object"] = objectId,
                ["https://www.w3.org/ns/activitystreams#actor"] = authorId
            }, inCollection?.DbId);
        }

        public async Task<List<APEntity>> FindRelevantObject(string objectType, string objectId, APEntity inCollection = null)
        {
            return await _search(new Dictionary<string, string> {
                ["rdf:type"] = objectType,
                ["https://www.w3.org/ns/activitystreams#object"] = objectId
            }, inCollection?.DbId);
        }

        public async Task<List<APEntity>> FindEntitiesWithFollowerId(string followerId)
        {
            return await _search(new Dictionary<string, string> {
                ["https://www.w3.org/ns/activitystreams#followers"] = followerId
            });
        }

        public async Task<List<APEntity>> FindEmojiLike(string like)
        {
            return await _searchLike(new Dictionary<string, string> {
                ["rdf:type"] = "http://joinmastodon.org/ns#Emoji"
            }, "https://www.w3.org/ns/activitystreams#name", like);
        }

        public async Task<List<APEntity>> FindUsersWithNameLike(string like)
        {
            return await _searchLike(new Dictionary<string, string> {}, "https://www.w3.org/ns/activitystreams#preferredUsername", like);
        }

        public async Task<List<APEntity>> FindEntitiesWithPreferredUsername(string username)
        {
            var reverseId = await _entityStore.ReverseAttribute("https://www.w3.org/ns/activitystreams#preferredUsername", false);
            if (reverseId == null) return new List<APEntity>();

            var b = await _connection.QueryAsync<APTripleEntity>("select a.* from \"TripleEntities\" a, \"Triples\" b where a.\"IsOwner\" = TRUE and b.\"SubjectId\" = a.\"IdId\" and b.\"SubjectEntityId\" = a.\"EntityId\" and b.\"PredicateId\" = @Predicate and b.\"Object\" = @Object",
                new { Predicate = reverseId.Value, Object = username });

            return await _entityStore.GetEntities(b.Select(a => a.EntityId).ToList());
        }
    }
}
