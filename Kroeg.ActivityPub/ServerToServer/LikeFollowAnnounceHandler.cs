using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Store;
using Kroeg.ActivityStreams;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using Kroeg.Services;
using Kroeg.EntityStore.Services;
using Kroeg.ActivityPub.BackgroundTasks;

namespace Kroeg.ActivityPub.ServerToServer
{
    public class LikeFollowAnnounceHandler : BaseHandler
    {
        private readonly CollectionTools _collection;
        private readonly ServerConfig _data;
        private readonly RelevantEntitiesService _relevantEntities;
        private readonly IServiceProvider _serviceProvider;
        private readonly DbConnection _connection;

        public LikeFollowAnnounceHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection, ServerConfig data, RelevantEntitiesService relevantEntities, IServiceProvider serviceProvider, DbConnection connection) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
            _data = data;
            _relevantEntities = relevantEntities;
            _serviceProvider = serviceProvider;
            _connection = connection;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type == "https://www.w3.org/ns/activitystreams#Follow")
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
                    id.AddClaim(new Claim("actor", Actor.Id));
                    claims.AddIdentity(id);

                    var handler = ActivatorUtilities.CreateInstance<GetEntityHandler>(_serviceProvider, claims, EntityStore);
                    var outbox = await EntityStore.GetEntity(Actor.Data["outbox"].First().Id, false);
                    await handler.ClientToServer(outbox, accept);
                }

                return true;
            }

            if (MainObject.Type != "https://www.w3.org/ns/activitystreams#Like" && MainObject.Type != "https://www.w3.org/ns/activitystreams#Announce") return true;

            var toFollowOrLike = await EntityStore.GetEntity(MainObject.Data["object"].Single().Id, false);
            if (toFollowOrLike == null)
            {
                await GetEntityTask.Make(MainObject.Data["object"].Single().Id, _connection);
                return true;
            }

            // sent to not the owner, so not updating!
            if (toFollowOrLike.Data["attributedTo"].Single().Id != Actor.Id) return true;

            string collectionId = null, objectToAdd = null;

            switch (MainObject.Type)
            {
                case "https://www.w3.org/ns/activitystreams#Like":
                    collectionId = toFollowOrLike.Data["likes"].SingleOrDefault()?.Id;
                    objectToAdd = MainObject.Id;
                    break;
                case "https://www.w3.org/ns/activitystreams#Announce":
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
