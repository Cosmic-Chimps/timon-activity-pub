using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class UndoActivityHandler : BaseHandler
    {
        private readonly CollectionTools _collection;

        public UndoActivityHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "https://www.w3.org/ns/activitystreams#Undo") return true;
            var toUndoId = MainObject.Data["object"].Single().Id;
            var toUndo = await EntityStore.GetEntity(toUndoId, false);

            if (toUndo == null || !toUndo.IsOwner) throw new InvalidOperationException("Object to undo does not exist!");
            if (toUndo.Type != "https://www.w3.org/ns/activitystreams#Like"
                && toUndo.Type != "https://www.w3.org/ns/activitystreams#Follow"
                && toUndo.Type != "https://www.w3.org/ns/activitystreams#Block")
                throw new InvalidOperationException("Cannot undo this type of object!");
            if (!toUndo.Data["actor"].Any(a => a.Id == Actor.Id)) throw new InvalidOperationException("You are not allowed to undo this activity!");

            var userData = Actor.Data;
            string targetCollectionId = null;
            if (toUndo.Type == "https://www.w3.org/ns/activitystreams#Block")
            {
                var blocksCollection = await EntityStore.GetEntity(userData["blocks"].Single().Id, false);
                var blockedCollection = await EntityStore.GetEntity(blocksCollection.Data["blocked"].SingleOrDefault()?.Id, false);

                await _collection.RemoveFromCollection(blocksCollection, toUndo);

                var blockedUser = toUndo.Data["object"].Single().Id;
                await _collection.RemoveFromCollection(blockedCollection, blockedUser);

                return true;
            }
            else if (toUndo.Type == "https://www.w3.org/ns/activitystreams#Like")
                targetCollectionId = userData["liked"].Single().Id;
            else if (toUndo.Type == "https://www.w3.org/ns/activitystreams#Follow")
                targetCollectionId = userData["following"].Single().Id;

            var targetCollection = await EntityStore.GetEntity(targetCollectionId, false);
            var targetId = MainObject.Data["object"].Single().Id;

            await _collection.RemoveFromCollection(targetCollection, targetId);
            return true;
        }
    }
}
