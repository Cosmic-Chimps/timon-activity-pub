using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;

namespace Kroeg.Server.Middleware.Handlers.ServerToServer
{
    public class FollowResponseHandler : BaseHandler
    {
        private readonly CollectionTools _collection;
        private readonly RelevantEntitiesService _relevantEntities;

        public FollowResponseHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection, RelevantEntitiesService relevantEntities) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
            _relevantEntities = relevantEntities;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "https://www.w3.org/ns/activitystreams#Accept") return true;
            var followObject = await EntityStore.GetEntity(MainObject.Data["object"].Single().Id, false);
            if (followObject == null)
            {
                followObject = (await _relevantEntities.FindRelevantObject("https://www.w3.org/ns/activitystreams#Follow", Actor.Id)).FirstOrDefault();
                if (followObject != null)
                {
                    MainObject.Data.Replace("object", ASTerm.MakeId(followObject.Id));
                    await EntityStore.StoreEntity(MainObject);
                }
            }

            if (followObject == null || followObject.Type != "https://www.w3.org/ns/activitystreams#Follow") return true;

            if (followObject.Data["object"].First().Id != MainObject.Data["actor"].First().Id) throw new InvalidOperationException("I won't let you do that, Starfox!");
            var followUser = followObject.Data["object"].First().Id;

            if (Actor.Id != followObject.Data["object"].First().Id) return true; // doesn't involve us, so meh

            if (!followObject.IsOwner) throw new InvalidOperationException("Follow isn't made on this server?");

            var relevant = await _relevantEntities.FindRelevantObject(followUser, "Reject", followObject.Id);
            if (relevant != null) throw new InvalidOperationException("Follow has already been Rejected before!");

            if (MainObject.Type == "https://www.w3.org/ns/activitystreams#Accept")
            {
                relevant = await _relevantEntities.FindRelevantObject(followUser, "Accept", followObject.Id);
                if (relevant != null) throw new InvalidOperationException("Follow has already been Accepted before!");
            }

            var following = await EntityStore.GetEntity(Actor.Data["following"].Single().Id, false);
            var user = await EntityStore.GetEntity(MainObject.Data["actor"].Single().Id, true);
            if (MainObject.Type == "https://www.w3.org/ns/activitystreams#Accept" && !await _collection.Contains(following, user.Id))
                await _collection.AddToCollection(following, user);
            else if (MainObject.Type == "https://www.w3.org/ns/activitystreams#Reject")
                await _collection.RemoveFromCollection(following, user);

            return true;
        }
    }
}
