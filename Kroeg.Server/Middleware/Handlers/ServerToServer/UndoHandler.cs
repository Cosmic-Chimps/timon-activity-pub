using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Middleware.Handlers.ServerToServer
{
    public class UndoHandler : BaseHandler
    {
        private readonly CollectionTools _collection;

        public UndoHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "https://www.w3.org/ns/activitystreams#Undo") return true;

            var toUndo = await EntityStore.GetEntity(MainObject.Data["object"].Single().Id, true);
            if (toUndo == null) return true; 

            string collectionId = null, objectToAdd = null;

            var toFollowOrLike = await EntityStore.GetEntity(toUndo.Data["object"].Single().Id, true);
            if (toFollowOrLike == null || !toFollowOrLike.IsOwner) return true; // can't undo side effects.
            if ((toUndo.Type == "https://www.w3.org/ns/activitystreams#Follow" && Actor.Id != toFollowOrLike.Id)
                || (toUndo.Type != "https://www.w3.org/ns/activitystreams#Follow" && toFollowOrLike.Data["attributedTo"].SingleOrDefault()?.Id != Actor.Id)) return true;

            if (toUndo.Type == "https://www.w3.org/ns/activitystreams#Follow")
            {
                collectionId = toFollowOrLike.Data["followers"].SingleOrDefault()?.Id;
                objectToAdd = toUndo.Data["actor"].Single().Id;
            }
            else if (toUndo.Type == "https://www.w3.org/ns/activitystreams#Like")
            {
                collectionId = toFollowOrLike.Data["likes"].SingleOrDefault()?.Id;
                objectToAdd = toUndo.Id;
            }
            else if (toUndo.Type == "https://www.w3.org/ns/activitystreams#Announce")
            {
                collectionId = toFollowOrLike.Data["shares"].SingleOrDefault()?.Id;
                objectToAdd = toUndo.Id;
            }

            if (collectionId == null) return true; // no way to store followers/likse

            var collection = await EntityStore.GetEntity(collectionId, false);
            await _collection.RemoveFromCollection(collection, objectToAdd);

            return true;
        }
    }
}
