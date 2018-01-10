using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Services;
using Kroeg.EntityStore.Store;
using Kroeg.Services;

namespace Kroeg.ActivityPub.ClientToServer
{
    public class ObjectWrapperHandler : BaseHandler
    {
        private readonly URLService _urlService;

        public ObjectWrapperHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, URLService urlService)
            : base(entityStore, mainObject, actor, targetBox, user)
        {
            _urlService = urlService;
        }

        public override async Task<bool> Handle()
        {
            if (EntityData.IsActivity(MainObject.Data)) return true;
            var data = MainObject.Data;

            var createActivity = new ASObject();
            createActivity.Type.Add("https://www.w3.org/ns/activitystreams#Create");
            createActivity["to"].AddRange(data["to"]);
            createActivity["bto"].AddRange(data["bto"]);
            createActivity["cc"].AddRange(data["cc"]);
            createActivity["bcc"].AddRange(data["bcc"]);
            createActivity["audience"].AddRange(data["audience"]);
            createActivity["actor"].Add(ASTerm.MakeId(Actor.Id));
            createActivity["object"].Add(ASTerm.MakeId(MainObject.Id));
            createActivity.Id = await _urlService.FindUnusedID(EntityStore, createActivity);

            var activityEntity = await EntityStore.StoreEntity(APEntity.From(createActivity, true));
            MainObject = activityEntity;

            return true;
        }
    }
}
