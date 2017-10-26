using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Kroeg.ActivityStreams;
using Microsoft.Extensions.DependencyInjection;
using Kroeg.Server.Configuration;

namespace Kroeg.Server.Middleware.Handlers.ServerToServer
{
    public class LikeFollowAnnounceHandler : BaseHandler
    {
        private readonly CollectionTools _collection;
        private readonly EntityData _data;
        private readonly RelevantEntitiesService _relevantEntities;
        private readonly IServiceProvider _serviceProvider;

        public LikeFollowAnnounceHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection, EntityData data, RelevantEntitiesService relevantEntities, IServiceProvider serviceProvider) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
            _data = data;
            _relevantEntities = relevantEntities;
            _serviceProvider = serviceProvider;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type == "Follow")
            {
                if (Actor.Data["manuallyApprovesFollowers"].Any(a => !(bool) a.Primitive))
                {
                    var accept = new ASObject();
                    accept.Type.Add("https://www.w3.org/ns/activitystreams#Accept");
                    accept.Replace("actor", ASTerm.MakeId(Actor.Id));
                    accept.Replace("object", ASTerm.MakeId(MainObject.Id));
                    accept["to"].AddRange(MainObject.Data["actor"]);

                    var claims = new ClaimsPrincipal();
                    var id = new ClaimsIdentity();
                    id.AddClaim(new Claim(JwtTokenSettings.ActorClaim, Actor.Id));
                    claims.AddIdentity(id);

                    var handler = ActivatorUtilities.CreateInstance<GetEntityMiddleware.GetEntityHandler>(_serviceProvider, claims, EntityStore);
                    var outbox = await EntityStore.GetEntity(Actor.Data["outbox"].First().Id, false);
                    await handler.ClientToServer(outbox, accept);
                }

                return true;
            }

            if (MainObject.Type != "Like" && MainObject.Type != "Announce") return true;

            var toFollowOrLike = await EntityStore.GetEntity(MainObject.Data["object"].Single().Id, false);
            if (toFollowOrLike == null || !toFollowOrLike.IsOwner) return true; // not going to update side effects now.

            // sent to not the owner, so not updating!
            if (toFollowOrLike.Data["attributedTo"].Single().Id != Actor.Id) return true;

            string collectionId = null, objectToAdd = null;

            switch (MainObject.Type)
            {
                case "Like":
                    collectionId = toFollowOrLike.Data["likes"].SingleOrDefault()?.Id;
                    objectToAdd = MainObject.Id;
                    break;
                case "Announce":
                    collectionId = toFollowOrLike.Data["shares"].SingleOrDefault()?.Id;
                    objectToAdd = MainObject.Id;
                    break;
            }

            if (collectionId == null) return true; // no way to store followers/likse

            var collection = await EntityStore.GetEntity(collectionId, false);
            var entityToAdd = await EntityStore.GetEntity(objectToAdd, true);

            if (entityToAdd == null) throw new InvalidOperationException("Can't like or announce a non-existant object!");

            await _collection.AddToCollection(collection, entityToAdd);

            return true;
        }
    }
}
