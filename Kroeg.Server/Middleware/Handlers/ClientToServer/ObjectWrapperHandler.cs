﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class ObjectWrapperHandler : BaseHandler
    {
        private readonly EntityData _entityData;

        public ObjectWrapperHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, EntityData entityData)
            : base(entityStore, mainObject, actor, targetBox, user)
        {
            _entityData = entityData;
        }

        public override async Task<bool> Handle()
        {
            if (_entityData.IsActivity(MainObject.Data)) return true;
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
            createActivity.Id = await _entityData.FindUnusedID(EntityStore, createActivity);

            var activityEntity = await EntityStore.StoreEntity(APEntity.From(createActivity, true));
            MainObject = activityEntity;

            return true;
        }
    }
}
