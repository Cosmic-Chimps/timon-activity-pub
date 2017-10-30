using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class AddRemoveActivityHandler : BaseHandler
    {
        private readonly CollectionTools _collection;

        public AddRemoveActivityHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "https://www.w3.org/ns/activitystreams#Remove" && MainObject.Type != "https://www.w3.org/ns/activitystreams#Add") return true;
            var activityData = MainObject.Data;

            var targetEntity = await EntityStore.GetEntity(activityData["target"].Single().Id, false);
            if (targetEntity == null)
                throw new InvalidOperationException("Cannot add or remove from a non-existant collection!");

            if (!targetEntity.IsOwner)
                throw new InvalidOperationException("Cannot add or remove from a collection I'm not owner of!");

            if (targetEntity.Type != "https://www.w3.org/ns/activitystreams#Collection" && targetEntity.Type != "https://www.w3.org/ns/activitystreams#OrderedCollection")
                throw new InvalidOperationException("Cannot add or remove from something that isn't a collection!");

            // XXX todo: add authorization on here

            var objectId = activityData["object"].Single().Id;

            if (MainObject.Type == "https://www.w3.org/ns/activitystreams#Add")
            {
                var objectEntity = await EntityStore.GetEntity(objectId, true);
                if (objectEntity == null)
                    throw new InvalidOperationException("Cannot add a non-existant object!");

                await _collection.AddToCollection(targetEntity, objectEntity);
            }
            else if (MainObject.Type == "https://www.w3.org/ns/activitystreams#Remove")
                await _collection.RemoveFromCollection(targetEntity, objectId);

            return true;
        }
    }
}
