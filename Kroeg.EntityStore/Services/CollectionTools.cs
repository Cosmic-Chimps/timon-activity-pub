using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.EntityStore.Store;
using Kroeg.Server.Tools;
using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using System.Data;
using Dapper;
using System.Data.Common;

namespace Kroeg.Server.Services
{
    public class CollectionTools
    {
        private readonly TripleEntityStore _entityStore;
        private readonly URLService _urlService;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly DbConnection _connection;
        private readonly INotifier _notifier;

        public CollectionTools(TripleEntityStore entityStore, URLService urlService, IServiceProvider serviceProvider, DbConnection connection, INotifier notifier)
        {
            _entityStore = entityStore;
            _urlService = urlService;
            _contextAccessor = (IHttpContextAccessor)serviceProvider.GetService(typeof(IHttpContextAccessor));
            _connection = connection;
            _notifier = notifier;
        }

        public async Task<int> Count(string id)
        {
            var entity = await _entityStore.GetEntity(id, true);
            if (entity.IsOwner)
                return await _connection.ExecuteScalarAsync<int>("select count(*) from \"CollectionItems\" where \"CollectionId\" = @Id", new { Id = entity.DbId });

            var data = entity.Data;
            if (data["totalItems"].Any())
            {
                var prim = data["totalItems"].Single().Primitive;
                if (prim is string) return int.Parse((string) prim);
                return (int) prim;
            }

            return -1;
        }

        private string _getUser() => _contextAccessor.HttpContext.User.FindFirstValue("actor");

        private static HashSet<string> _audienceIds = new HashSet<string> {
            "https://www.w3.org/ns/activitystreams#to",
            "https://www.w3.org/ns/activitystreams#bto",
            "https://www.w3.org/ns/activitystreams#cc",
            "https://www.w3.org/ns/activitystreams#bcc",
            "https://www.w3.org/ns/activitystreams#audience",
            "https://www.w3.org/ns/activitystreams#attributedTo",
            "https://www.w3.org/ns/activitystreams#actor"
        };

        private async Task<IEnumerable<CollectionItem>> _filterAudience(string user, bool isOwner, int dbId, int count, int under = int.MaxValue, int above = int.MinValue, RelevantEntitiesService.IQueryStatement query = null)
        {
            var postfix = "order by \"CollectionItemId\" desc " + (count > 0 ? $"limit {count}" : "");
            if (isOwner && query == null)
                return await _connection.QueryAsync<CollectionItem>("select * from \"CollectionItems\" WHERE \"CollectionItemId\" < @Under and \"CollectionItemId\" > @Above and \"CollectionId\" = @DbId " + postfix, new { Under = under, Above = above, DbId = dbId });

            int? userId = null;
            if (user!= null) userId = await _entityStore.ReverseAttribute(user, false);
            if (userId == null && query == null)
                return await _connection.QueryAsync<CollectionItem>("select * from \"CollectionItems\" WHERE \"CollectionItemId\" < @Under and \"CollectionItemId\" > @Above and \"IsPublic\" = TRUE and \"CollectionId\" = @DbId " + postfix, new { Under = under, Above = above, DbId = dbId });

             var ids = new List<int>();
             foreach (var audienceId in _audienceIds)
             {
                 var id = await _entityStore.ReverseAttribute(audienceId, false);
                 if (id.HasValue)
                     ids.Add(id.Value);
             }


            var queryMap = new Dictionary<string, int>();
            if (query != null)
            {
                foreach (var typeId in query.RequiredProperties)
                {
                    var id = await _entityStore.ReverseAttribute(typeId, true);
                    queryMap[typeId] = id.Value;
                }
            }


// select c.* from "CollectionItems" c, "TripleEntities" e WHERE e."EntityId" = c."ElementId" and "CollectionItemId" < @Under and exists(select 1 from "Triples" where "PredicateId" = any(@Ids) and "AttributeId" = @UserId and "SubjectId" = e."IdId" and "SubjectEntityId" = e."EntityId" limit 1)
            return await _connection.QueryAsync<CollectionItem>(
                "select c.* from \"CollectionItems\" c, \"TripleEntities\" a WHERE a.\"EntityId\" = c.\"ElementId\" and \"CollectionItemId\" < @Under and \"CollectionItemId\" > @Above and \"CollectionId\" = @DbId "
                + (isOwner ? "" : userId == null ? " and c.\"IsPublic\" = true" : "and exists(select 1 from \"Triples\" where \"PredicateId\" = any(@Ids) and \"AttributeId\" = @UserId and \"SubjectId\" = a.\"IdId\" and \"SubjectEntityId\" = a.\"EntityId\" limit 1) ")
                + (query == null ? " " : $" and {query.BuildSQL(queryMap)} ")
                 + "order by c.\"CollectionItemId\" desc " + (count > 0 ? $"limit {count}" : ""),
                 new { Under = under, Above = above, Ids = ids, UserId = userId ?? 0, DbId = dbId }
            );
        }

        public class EntityCollectionItem {
            public int CollectionItemId { get; set; }
            public APEntity Entity { get; set; }
        }

        public async Task<List<EntityCollectionItem>> GetItems(string id, int fromId = int.MaxValue, int toId = int.MinValue, int count = 10, RelevantEntitiesService.IQueryStatement query = null)
        {
            var isOwner = false;
            var entity = await _entityStore.GetEntity(id, false);
            var user = _getUser();
            if (entity != null && entity.Data["attributedTo"].Any(a => a.Id == user)) isOwner = true;


            var entities = await _filterAudience(user, isOwner, entity.DbId, count, fromId, toId, query);
            return (await _entityStore.GetEntities(entities.Select(a => a.ElementId).ToList())).Zip(entities, (a, b) => new EntityCollectionItem { CollectionItemId = b.CollectionItemId, Entity = a }).ToList();
        }

        public async Task<List<EntityCollectionItem>> GetAll(string id)
        {
            var entity = await _entityStore.GetEntity(id, false);
            var user = _getUser();
            var isOwner = entity != null && entity.Data["attributedTo"].Any(a => a.Id == user);

            var entities = await _filterAudience(user, isOwner, entity.DbId, -1);
            return (await _entityStore.GetEntities(entities.Select(a => a.ElementId).ToList())).Zip(entities, (a, b) => new EntityCollectionItem { CollectionItemId = b.CollectionItemId, Entity = a }).ToList();
        }

        public async Task<EntityCollectionItem> GetCollectionItem(int id)
        {
            var collectionItem = await _connection.QuerySingleOrDefaultAsync<CollectionItem>("select * from \"CollectionItems\" where \"CollectionItemId\" = @Id", new { Id = id });
            if (collectionItem == null) return null;
            return new EntityCollectionItem { CollectionItemId = id, Entity = await _entityStore.GetEntity(collectionItem.ElementId) };
        }

        public async Task<List<APEntity>> CollectionsContaining(string containId, string type = null)
        {
            var element = await _entityStore.GetEntity(containId, false);
            if (element == null) return new List<APEntity>();

            IEnumerable<CollectionItem> collectionItems;
            if (type == null)
                collectionItems = await _connection.QueryAsync<CollectionItem>("select * from \"CollectionItems\" where \"ElementId\" = @Id", new { Id = element.DbId });
            else
                collectionItems = await _connection.QueryAsync<CollectionItem>("select a.* from \"CollectionItems\" a where a.\"ElementId\" = @Id and exists(select 1 from \"TripleEntities\" ent where a.\"CollectionId\" = ent.\"EntityId\" and ent.\"Type\" = @Type)", new { Id = element.DbId, Type = type });

            return await _entityStore.GetEntities(collectionItems.Select(a => a.CollectionId).ToList());
        }

        private static bool _isPublic(ASObject @object)
        {
            var targetIds = new List<string>();

            targetIds.AddRange(@object["to"].Select(a => a.Id));
            targetIds.AddRange(@object["bto"].Select(a => a.Id));

            return targetIds.Contains("https://www.w3.org/ns/activitystreams#Public") && !@object["unlisted"].Any(a => (bool) a.Primitive);
        }

        public async Task<CollectionItem> AddToCollection(APEntity collection, APEntity entity)
        {
            var ci = new CollectionItem
            {
                CollectionId = collection.DbId,
                ElementId = entity.DbId,
                IsPublic = _isPublic(entity.Data) || EntityData.IsActor(entity.Data)
            };


            await _connection.ExecuteAsync("insert into \"CollectionItems\" (\"CollectionId\", \"ElementId\", \"IsPublic\") values (@CollectionId, @ElementId, @IsPublic)", ci);
            await _notifier.Notify($"collection/{collection.Id}", entity.Id);

            return ci;
        }

        private async Task<int?> _getId(string id)
        {
            var otherEntity = await _entityStore.ReverseAttribute(id, false);
            if (otherEntity == null) return null;

            return await _connection.ExecuteScalarAsync<int>("select \"EntityId\" from \"TripleEntities\" where \"IdId\" = @IdId limit 1", new { IdId = otherEntity.Value });
        }

        public async Task<bool> Contains(APEntity collection, string otherId)
        {
            var otherEntity = await _getId(otherId);
            if (otherEntity == null) return false;

            return await _connection.ExecuteScalarAsync<bool>("select exists(select 1 from \"CollectionItems\" where \"CollectionId\" = @CollectionId and \"ElementId\" = @ElementId)", new { CollectionId = collection.DbId, ElementId = otherEntity.Value });
        }

        public async Task RemoveFromCollection(APEntity collection, string id)
        {
            var otherEntity = await _getId(id);
            if (otherEntity == null) return;

            await _connection.ExecuteAsync("delete from \"CollectionItems\" where \"CollectionId\" = @CollectionId and \"ElementId\" = @ElementId", new { CollectionId = collection.DbId, ElementId = otherEntity.Value });
        }

        public async Task RemoveFromCollection(APEntity collection, APEntity entity)
        {
            await RemoveFromCollection(collection, entity.Id);
        }

        public async Task<APEntity> NewCollection(IEntityStore store, ASObject mold = null, string type = null, string superItem = null)
        {
            if (mold == null) mold = new ASObject();
            mold.Type.Add("https://www.w3.org/ns/activitystreams#OrderedCollection");
            var owner = mold.Id == null;
            if (mold.Id == null)
                mold.Id = await _urlService.FindUnusedID(store, mold, type?.Replace("_", "").ToLower(), superItem);

            var entity = new APEntity
            {
                Id = mold.Id,
                Data = mold,
                Type = type ?? "https://www.w3.org/ns/activitystreams#OrderedCollection",
                IsOwner = owner
            };

            return entity;
        }
    }
}
