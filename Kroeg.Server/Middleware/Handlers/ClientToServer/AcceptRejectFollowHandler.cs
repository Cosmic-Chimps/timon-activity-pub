using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class AcceptRejectFollowHandler : BaseHandler
    {
        private readonly EntityData _data;
        private readonly CollectionTools _collection;

        public AcceptRejectFollowHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, EntityData data, CollectionTools collection) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _data = data;
            _collection = collection;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "https://www.w3.org/ns/activitystreams#Accept"
                && MainObject.Type != "https://www.w3.org/ns/activitystreams#Reject") return true;

            var subObject = await EntityStore.GetEntity(MainObject.Data["object"].Single().Id, true);
            var requestedUser = await EntityStore.GetEntity(subObject.Data["actor"].First().Id, true);

            if (subObject.Type != "https://www.w3.org/ns/activitystreams#Follow") return true;

            if (subObject.Data["object"].Single().Id != Actor.Id) return true;
            
            bool isAccept = MainObject.Type == "https://www.w3.org/ns/activitystreams#Accept";
            var followers = await EntityStore.GetEntity(Actor.Data["followers"].Single().Id, false);

            if (isAccept && !await _collection.Contains(followers.Id, requestedUser.Id))
                await _collection.AddToCollection(followers, requestedUser);
            if (!isAccept && await _collection.Contains(followers.Id, requestedUser.Id))
                await _collection.RemoveFromCollection(followers, requestedUser);
            return true;
        }
    }
}
