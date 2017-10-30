using Kroeg.Server.BackgroundTasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Data;
using System.Data.Common;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class WebSubHandler : BaseHandler
    {
        private readonly DbConnection _connection;

        public WebSubHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, DbConnection connection) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _connection = connection;
        }

        public override async Task<bool> Handle()
        {
            var activity = MainObject;
            if (MainObject.Type == "https://www.w3.org/ns/activitystreams#Undo")
            {
                var subObject = await EntityStore.GetEntity(activity.Data["object"].First().Id, false);
                if (subObject?.Type != "https://www.w3.org/ns/activitystreams#Follow") return true;

                activity = subObject;
            }
            else if (MainObject.Type != "https://www.w3.org/ns/activitystreams#Follow") return true;

            var target = await EntityStore.GetEntity(activity.Data["object"].First().Id, true);
            if (target == null) return true; // can't really fix subscriptions on a thing that doesn't exist

            var hubUrl = (string) target.Data["_:hubUrl"].SingleOrDefault()?.Primitive;
            if (hubUrl == null) return true;

            await WebSubBackgroundTask.Make(new WebSubBackgroundData { Unsubscribe = MainObject.Type == "https://www.w3.org/ns/activitystreams#Undo", ToFollowID = target.DbId, ActorID = MainObject.Data["actor"].Single().Id }, _connection);

            return true;
        }
    }
}
