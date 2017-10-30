﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Microsoft.AspNetCore.Http;
using System;
using Kroeg.Server.Salmon;

namespace Kroeg.Server.Tools
{
    public class EntityFlattener
    {
        private readonly EntityData _configuration;
        private readonly APContext _context;

        public EntityFlattener(APContext context, EntityData configuration)
        {
            _configuration = configuration;
            _context = context;
        }

        public async Task<APEntity> FlattenAndStore(IEntityStore store, ASObject @object, bool generateId = true, Dictionary<string, APEntity> dict = null)
        {
            dict = dict ?? new Dictionary<string, APEntity>();
            var main = await Flatten(store, @object, generateId, dict);

            foreach (var entity in dict.ToArray())
                dict[entity.Key] = await store.StoreEntity(entity.Value);

            return dict[main.Id];
        }

        public async Task<APEntity> Flatten(IEntityStore store, ASObject @object, bool generateId = true, Dictionary<string, APEntity> flattened = null)
        {
            if (flattened == null)
                flattened = new Dictionary<string, APEntity>();

            var mainEntity = await _flatten(store, @object, generateId, flattened);

            return flattened[mainEntity.Id];
        }

        public async Task<ASObject> Unflatten(IEntityStore store, APEntity entity, int depth = 3, Dictionary<string, APEntity> mapped = null)
        {
            if (mapped == null)
                mapped = new Dictionary<string, APEntity>();

            if (entity.Id != null)
            {
                var e = await store.GetEntity(entity.Id, false);
                if (e?.IsOwner == true) entity.IsOwner = true;
            }

            var unflattened = await _unflatten(store, entity, depth, mapped, _configuration.UnflattenRemotely);

            return unflattened;
        }

        private static readonly HashSet<string> IdHolding = new HashSet<string>
        {
            "subject", "relationship", "actor", "attributedTo", "attachment", "bcc", "bto", "cc", "context", "current", "first", "generator", "icon", "image", "inReplyTo", "items", "instrument", "orderedItems", "last", "location", "next", "object", "oneOf", "anyOf", "origin", "prev", "preview", "replies", "result", "audience", "partOf", "tag", "target", "to", "describes", "formerType", "streams", "publicKey"
        };

        private static readonly HashSet<string> _mayNotUnflatten = new HashSet<string>
        {
            "https://www.w3.org/ns/activitystreams#next", "https://www.w3.org/ns/activitystreams#prev",
            "https://www.w3.org/ns/activitystreams#first", "https://www.w3.org/ns/activitystreams#last",
            "https://www.w3.org/ns/activitystreams#bcc", "https://www.w3.org/ns/activitystreams#bto",
            "https://www.w3.org/ns/activitystreams#cc", "https://www.w3.org/ns/activitystreams#to",
            "https://www.w3.org/ns/activitystreams#audience", "http://www.w3.org/ns/ldp#inbox",
            "https://www.w3.org/ns/activitystreams#outbox", "https://www.w3.org/ns/activitystreams#followers",
            "https://www.w3.org/ns/activitystreams#following", "https://www.w3.org/ns/activitystreams#followers",
            "https://www.w3.org/ns/activitystreams#partOf"
        };

        private static readonly HashSet<string> UnflattenIfOwner = new HashSet<string>
        {
            "endpoints", "publicKey"
        };


        private ASObject _getEndpoints(APEntity entity)
        {
            var data = entity.Data;
            data.Replace("endpoints", ASTerm.MakeId(entity.Id + "#endpoints"));
            data.Replace("publicKey", ASTerm.MakeId(entity.Id + "#key"));
            return data;
        }

        private async Task<APEntity> _flatten(IEntityStore store, ASObject @object, bool generateId, IDictionary<string, APEntity> entities, string parentId = null)
        {

            var entity = new APEntity();
            if (@object["href"].Any()) // Link
            {
                return null;
            }

            if (@object.Id == null)
            {
                if (!generateId) return null;
                @object.Id = await _configuration.FindUnusedID(store, @object, null, parentId);
                entity.IsOwner = true;
            }

            entity.Id = @object.Id;
            var t = @object.Type.FirstOrDefault();
            if (t?.StartsWith("_") != false && t?.StartsWith("_:") != true) t = "Unknown";
            entity.Type = t;

            foreach (var kv in @object)
            {
                if (!IdHolding.Contains(kv.Key)) continue;
                foreach (var value in kv.Value)
                {
                    if (value.SubObject == null) continue;

                    var subObject = await _flatten(store, value.SubObject, generateId, entities, entity.Id);
                    if (subObject == null) continue;

                    value.Id = subObject.Id;
                    value.Primitive = null;
                    value.SubObject = null;
                }
            }

            entity.Data = @object;
            entities[entity.Id] = entity;

            return entity;
        }


        private static HashSet<string> _avoidFlatteningTypes = new HashSet<string> {
            "https://www.w3.org/ns/activitystreams#OrderedCollection", "https://www.w3.org/ns/activitystreams#Collection",
            "OrderedCollection", "Collection",
            "_replies", "_likes", "_shares", "_:LazyLoad"
        };

        private async Task<ASObject> _unflatten(IEntityStore store, APEntity entity, int depth, IDictionary<string, APEntity> alreadyMapped, bool remote)
        {
            if (depth == 0)
                return entity.Data;

            var @object = entity.Data;
            if (_configuration.IsActor(@object) && entity.IsOwner)
                @object = _getEndpoints(entity);

            var myid = @object.Id;
            if (myid != null)
                alreadyMapped[myid] = entity;

            @object["bto"].Clear();
            @object["bcc"].Clear();

            foreach (var kv in @object)
            {
                foreach (var value in kv.Value)
                {
                    if (value.SubObject != null) value.SubObject = await _unflatten(store, APEntity.From(value.SubObject), depth - 1, alreadyMapped, remote);
                    if (value.Id == null) continue;
                    if (_mayNotUnflatten.Contains(kv.Key) && (!entity.IsOwner || !UnflattenIfOwner.Contains(kv.Key))) continue;
                    var id = value.Id;

                    if (alreadyMapped.ContainsKey(id)) continue;

                    var obj = await store.GetEntity(id, false);
                    if (obj == null || _avoidFlatteningTypes.Contains(obj.Type) || (!remote && !obj.IsOwner)) continue;
                    value.Primitive = null;
                    value.Id = null;
                    value.SubObject = await _unflatten(store, obj, depth - 1, alreadyMapped, remote);
                }
            }

            return @object;
        }
    }
}
