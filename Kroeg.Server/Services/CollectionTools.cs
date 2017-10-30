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

namespace Kroeg.Server.Services
{
    public class CollectionTools
    {
        private readonly APContext _context;
        private readonly TripleEntityStore _entityStore;
        private readonly EntityData _configuration;
        private readonly IHttpContextAccessor _contextAccessor;

        public CollectionTools(APContext context, TripleEntityStore entityStore, EntityData configuration, IServiceProvider serviceProvider)
        {
            _context = context;
            _entityStore = entityStore;
            _configuration = configuration;
            _contextAccessor  = (IHttpContextAccessor)serviceProvider.GetService(typeof(IHttpContextAccessor));
        }

        public async Task<int> Count(string id)
        {
            var entity = await _entityStore.GetEntity(id, true);
            if (entity.IsOwner)
                return await _context.CollectionItems.CountAsync(a => a.CollectionId == entity.DbId);

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

        private async Task<IQueryable<CollectionItem>> _filterAudience(string user, bool isOwner, IQueryable<CollectionItem> entities, int count)
        {
            if (isOwner)
                if (count > 0)
                    return entities.Take(count);
                else
                    return entities;
            var ids = new List<int>();
            foreach (var audienceId in _audienceIds)
            {
                var id = await _entityStore.ReverseAttribute(audienceId, false);
                if (id.HasValue)
                    ids.Add(id.Value);
            }

            int? userId = null;
            if (user!= null) userId = await _entityStore.ReverseAttribute(user, false);
            var res = entities;
            if (userId == null)
                res = entities.Where(a => a.IsPublic);
            else
                res = entities.Where(a => a.IsPublic || a.Element.Triples.Any(b => ids.Contains(b.PredicateId) && b.SubjectId == a.Element.IdId && b.AttributeId == userId));

            if (count > 0)
                return res.Take(count);
            return res;
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


            IQueryable<CollectionItem> data = _context.CollectionItems.Where(a => a.CollectionId == entity.DbId && a.CollectionItemId < fromId).OrderByDescending(a => a.CollectionItemId);
            data = await _filterAudience(user, isOwner, data, count);

            var collectionItems = await data.ToListAsync();
            return (await _entityStore.GetEntities(collectionItems.Select(a => a.ElementId).ToList())).Zip(collectionItems, (a, b) => new EntityCollectionItem { CollectionItemId = b.CollectionItemId, Entity = a}).ToList();
        }

        public async Task<List<EntityCollectionItem>> GetAll(string id)
        {
            var entity = await _entityStore.GetEntity(id, false);
            var user = _getUser();
            var isOwner = entity != null && entity.Data["attributedTo"].Any(a => a.Id == user);

            IQueryable<CollectionItem> list = _context.CollectionItems.Where(a => a.CollectionId == entity.DbId).OrderByDescending(a => a.CollectionItemId);
            list = await _filterAudience(user, isOwner, list, -1);

            var collectionItems = await list.ToListAsync();
            return (await _entityStore.GetEntities(collectionItems.Select(a => a.ElementId).ToList())).Zip(collectionItems, (a, b) => new EntityCollectionItem { CollectionItemId = b.CollectionItemId, Entity = a}).ToList();
        }

        public async Task<List<APEntity>> CollectionsContaining(string containId, string type = null)
        {
            var idString = await _entityStore.ReverseAttribute(containId, false);
            if (idString == null) return new List<APEntity> {};

            var collectionItems = _context.CollectionItems.Where(a => a.ElementId == idString.Value);
            if (type != null) collectionItems = collectionItems.Where(a => a.Collection.Type == type);

            return await _entityStore.GetEntities(await collectionItems.Select(a => a.CollectionId).ToListAsync());
        }

        public async Task<CollectionItem> AddToCollection(APEntity collection, APEntity entity)
        {
            var ci = new CollectionItem
            {
                CollectionId = collection.DbId,
                ElementId = entity.DbId,
                IsPublic = DeliveryService.IsPublic(entity.Data) || _configuration.IsActor(entity.Data)
            };

            await _context.CollectionItems.AddAsync(ci);

            return ci;
        }

        public async Task<bool> Contains(APEntity collection, string otherId)
        {
            var otherEntity = await _entityStore.ReverseAttribute(otherId, false);
            if (otherEntity == null) return false;

            return await _context.CollectionItems.AnyAsync(a => a.CollectionId == collection.DbId && a.ElementId == otherEntity.Value);
        }

        public async Task RemoveFromCollection(APEntity collection, string id)
        {
            var otherEntity = await _entityStore.ReverseAttribute(id, false);
            if (otherEntity == null) return;

            var item = await _context.CollectionItems.FirstOrDefaultAsync(a => a.CollectionId == collection.DbId && a.ElementId == otherEntity.Value);
            if (item != null)
                _context.CollectionItems.Remove(item);
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
