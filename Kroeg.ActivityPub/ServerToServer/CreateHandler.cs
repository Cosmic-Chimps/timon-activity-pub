using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.EntityStore.Store;
using Kroeg.Server.Tools;
using Kroeg.ActivityStreams;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;

namespace Kroeg.Server.Middleware.Handlers.ServerToServer
{
    public class CreateHandler : BaseHandler
    {
        private readonly CollectionTools _collection;

        public CreateHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "https://www.w3.org/ns/activitystreams#Create") return true;
            var obj = await EntityStore.GetEntity(MainObject.Data["object"].Single().Id, false);
            if (obj == null)
                return true;

            if (!obj.Data["inReplyTo"].Any()) return true;
            var inReply = await EntityStore.GetEntity(obj.Data["inReplyTo"].Single().Id, false);
            if (inReply == null || !inReply.IsOwner) return true;
            if (inReply.Data["attributedTo"].Single().Id != Actor.Id) return true;

            var collectionId = inReply.Data["replies"].SingleOrDefault()?.Id;
            if (collectionId == null) return true; // no way to store replies

            var collection = await EntityStore.GetEntity(collectionId, false);

            await _collection.AddToCollection(collection, obj);
            return true;
        }
    }
}
