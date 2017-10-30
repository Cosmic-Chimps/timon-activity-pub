﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Middleware.Handlers.ServerToServer
{
    public class VerifyOwnershipHandler : BaseHandler
    {
        public VerifyOwnershipHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user) : base(entityStore, mainObject, actor, targetBox, user)
        {
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "Update" && MainObject.Type != "Delete" && MainObject.Type != "Create" && MainObject.Type != "Undo") return true;

            await Task.Yield();

            var idToVerify = MainObject.Data["object"].Single().Id;
            var idToCheckAgainst = MainObject.Id;

            if (!MainObject.Data["actor"].Any()) throw new InvalidOperationException("Activity has no actor!");

            if (new Uri(idToVerify).Host != new Uri(idToCheckAgainst).Host)
                throw new InvalidOperationException("Hostname of the Activity isn't equal to the hostname of the Object!");

            return true;
        }
    }
}
