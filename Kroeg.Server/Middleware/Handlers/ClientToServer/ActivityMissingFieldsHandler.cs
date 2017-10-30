﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Configuration;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class ActivityMissingFieldsHandler : BaseHandler
    {
        public ActivityMissingFieldsHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user) : base(entityStore, mainObject, actor, targetBox, user)
        {
        }

        public override async Task<bool> Handle()
        {
            await Task.Yield();

            var data = MainObject.Data;
            if (data["actor"].Count != 1)
                throw new InvalidOperationException("Cannot create an activity with no or more than one actor!");
            if (data["actor"].First().Id != User.FindFirstValue(JwtTokenSettings.ActorClaim)) 
                throw new InvalidOperationException("Cannot create an activity with an actor that isn't the one you log in to");

            // add published and updated.
            data.Replace("published", ASTerm.MakeDateTime(DateTime.Now));

            MainObject.Data = data;

            return true;
        }
    }
}
