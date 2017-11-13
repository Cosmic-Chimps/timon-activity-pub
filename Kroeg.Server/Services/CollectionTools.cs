using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Kroeg.Server.Configuration;
using System.Data;
using Dapper;
using System.Data.Common;

namespace Kroeg.Server.Services
{
    public class CollectionTools
    {
        private readonly TripleEntityStore _entityStore;
        private readonly EntityData _configuration;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly DbConnection _connection;
        private readonly INotifier _notifier;

        public CollectionTools(TripleEntityStore entityStore, EntityData configuration, IServiceProvider serviceProvider, DbConnection connection, INotifier notifier)
        {
            _entityStore = entityStore;
            _configuration = configuration;
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
                return (int) data["totalItems"].Single().Primitive;

            return -1;
        }

        private string _getUser() => _contextAccessor.HttpContext.User.FindFirstValue(JwtTokenSettings.ActorClaim);

        private static HashSet<string> _audienceIds = new HashSet<string> {
            "https://www.w3.org/ns/activitystreams#to",
            "https://www.w3.org/ns/activitystreams#bto",
            "https://www.w3.org/ns/activitystreams#cc",
            "https://www.w3.org/ns/activitystreams#bcc",
            "https://www.w3.org/ns/activitystreams#audience",
            "https://www.w3.org/ns/activitystreams#attributedTo",
            "https://www.w3.org/ns/activitystreams#actor"
        };

        private async Task<IEnumerable<CollectionItem>> _filterAudience(string user, bool isOwner, int dbId, int count, int under = int.MaxValue)
        {
            var postfix = "order by \"CollectionItemId\" desc " + (count > 0 ? $"limit {count}" : "");
            if (isOwner)
                return await _connection.QueryAsync<CollectionItem>("select * from \"CollectionItems\" WHERE \"CollectionItemId\" < @Under and \"CollectionId\" = @DbId " + postfix, new { Under = under, DbId = dbId });

            int? userId = null;
            if (user!= null) userId = await _entityStore.ReverseAttribute(user, false);
            if (userId == null)
                return await _connection.QueryAsync<CollectionItem>("select * from \"CollectionItems\" WHERE \"CollectionItemId\" < @Under and \"IsPublic\" = TRUE and \"CollectionId\" = @DbId " + postfix, new { Under = under, DbId = dbId });

             var ids = new List<int>();
             foreach (var audienceId in _audienceIds)
             {
                 var id = await _entityStore.ReverseAttribute(audienceId, false);
                 if (id.HasValue)
                     ids.Add(id.Value);
             }


// select c.* from "CollectionItems" c, "TripleEntities" e WHERE e."EntityId" = c."ElementId" and "CollectionItemId" < @Under and exists(select 1 from "Triples" where "PredicateId" = any(@Ids) and "AttributeId" = @UserId and "SubjectId" = e."IdId" and "SubjectEntityId" = e."EntityId" limit 1)
            return await _connection.QueryAsync<CollectionItem>(
                "select c.* from \"CollectionItems\" c, \"TripleEntities\" e WHERE e.\"EntityId\" = c.\"ElementId\" and \"CollectionItemId\" < @Under and \"CollectionId\" = @DbId"
                + " and exists(select 1 from \"Triples\" where \"PredicateId\" = any(@Ids) and \"AttributeId\" = @UserId and \"SubjectId\" = e.\"IdId\" and \"SubjectEntityId\" = e.\"EntityId\" limit 1) "
                 + "order by c.\"CollectionItemId\" desc " + (count > 0 ? $"limit {count}" : ""),
                 new { Under = under, Ids = ids, UserId = userId.Value, DbId = dbId }
            );
        }

        public class EntityCollectionItem {
            public int CollectionItemId { get; set; }
            public APEntity Entity { get; set; }
        }

        public async Task<List<EntityCollectionItem>> GetItems(string id, int fromId = int.MaxValue, int count = 10)
        {
            var isOwner = false;
            var entity = await _entityStore.GetEntity(id, false);
            var user = _getUser();
            if (entity != null && entity.Data["attributedTo"].Any(a => a.Id == user)) isOwner = true;


            var entities = await _filterAudience(user, isOwner, entity.DbId, count, fromId);
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

        public async Task<CollectionItem> AddToCollection(APEntity collection, APEntity entity)
        {
            var ci = new CollectionItem
            {
                CollectionId = collection.DbId,
                ElementId = entity.DbId,
                IsPublic = DeliveryService.IsPublic(entity.Data) || _configuration.IsActor(entity.Data)
            };


            await _connection.ExecuteAsync("insert into \"CollectionItems\" (\"CollectionId\", \"ElementId\", \"IsPublic\") values (@CollectionId, @ElementId, @IsPublic)", ci);
            await _notifier.Notify($"collection/{collection.Id}", entity.Id);

            return ci;
        }

        public async Task<bool> Contains(APEntity collection, string otherId)
        {
            var otherEntity = await _entityStore.ReverseAttribute(otherId, false);
            if (otherEntity == null) return false;

            return await _connection.ExecuteScalarAsync<bool>("select exists(select 1 from \"CollectionItems\" where \"CollectionId\" = @CollectionId and \"ElementId\" = @ElementId)", new { CollectionId = collection.DbId, ElementId = otherEntity.Value });
        }

        public async Task RemoveFromCollection(APEntity collection, string id)
        {
            var otherEntity = await _entityStore.ReverseAttribute(id, false);
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
                mold.Id = await _configuration.FindUnusedID(store, mold, type?.Replace("_", "").ToLower(), superItem);

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
