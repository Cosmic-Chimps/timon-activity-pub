﻿using System.Collections.Generic;
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
        private readonly IEntityStore _entityStore;
        private readonly EntityData _configuration;
        private readonly IHttpContextAccessor _contextAccessor;

        public CollectionTools(APContext context, IEntityStore entityStore, EntityData configuration, IServiceProvider serviceProvider)
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
                return await _context.CollectionItems.CountAsync(a => a.CollectionId == id);

            var data = entity.Data;
            if (data["totalItems"].Any())
                return (int) data["totalItems"].Single().Primitive;

            return -1;
        }

        private string _getUser() => _contextAccessor.HttpContext.User.FindFirstValue(JwtTokenSettings.ActorClaim);

        private bool _verifyAudience(string user, CollectionItem entity)
        {
            if (entity.IsPublic) return true;
            if (_configuration.IsActor(entity.Element.Data)) return true;
            var audience = DeliveryService.GetAudienceIds(entity.Element.Data);
            return audience.Contains(user);
        }

        public async Task<List<CollectionItem>> GetItems(string id, int fromId = int.MaxValue, int count = 10)
        {
            var isOwner = false;
            var entity = await _context.Entities.FirstOrDefaultAsync(a => a.Id == id && a.IsOwner);
            var user = _getUser();
            if (entity != null && entity.Data["attributedTo"].Any(a => a.Id == user)) isOwner = true;

            IQueryable<CollectionItem> data = _context.CollectionItems.Where(a => a.CollectionId == id && a.CollectionItemId < fromId).Include(a => a.Element).OrderByDescending(a => a.CollectionItemId);
            if (user == null)
                return await data.Where(a => a.IsPublic).Take(count).ToListAsync();
            else if (isOwner)
                return await data.Take(count).ToListAsync();
            else
            {
                return (await data.ToListAsync()).Where((a) => _verifyAudience(user, a)).Take(count).ToList();
            }
        }

        public async Task<List<APEntity>> GetAll(string id)
        {
            var entity = await _context.Entities.FirstOrDefaultAsync(a => a.Id == id && a.IsOwner);
            var user = _getUser();

            IQueryable<CollectionItem> list = _context.CollectionItems.Where(a => a.CollectionId == id).Include(a => a.Element).OrderByDescending(a => a.CollectionItemId);
            if (user == null)
                list = list.Where(a => a.IsPublic);

            return await list.Select(a => a.Element).ToListAsync();
        }

        public async Task<List<APEntity>> CollectionsContaining(string containId, string type = null)
        {
            var collectionItems = _context.CollectionItems.Where(a => a.ElementId == containId).Select(a => a.Collection);
            if (type != null) collectionItems = collectionItems.Where(a => a.Type == type);
            return await collectionItems.ToListAsync();
        }

        public async Task<CollectionItem> AddToCollection(APEntity collection, APEntity entity)
        {
            var ci = new CollectionItem
            {
                Collection = collection,
                Element = entity,
                IsPublic = DeliveryService.IsPublic(entity.Data) || _configuration.IsActor(entity.Data)
            };

            await _context.CollectionItems.AddAsync(ci);

            return ci;
        }

        public async Task<bool> Contains(string collection, string otherId) => await _context.CollectionItems.AnyAsync(a => a.CollectionId == collection && a.ElementId == otherId);

        public async Task RemoveFromCollection(APEntity collection, string id)
        {
            var item = await _context.CollectionItems.FirstOrDefaultAsync(a => a.CollectionId == collection.Id && a.ElementId == id);
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
                Type = type ?? "OrderedCollection",
                IsOwner = owner
            };

            return entity;
        }
    }
}
