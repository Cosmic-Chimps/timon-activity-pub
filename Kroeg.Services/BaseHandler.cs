using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Store;

namespace Kroeg.Services
{
    public abstract class BaseHandler
    {
        protected readonly IEntityStore  EntityStore;
        protected readonly APEntity Actor;
        protected readonly APEntity TargetBox;
        protected readonly ClaimsPrincipal User;

        public APEntity MainObject { get; protected set; }

        protected BaseHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user)
        {
            EntityStore = entityStore;
            MainObject = mainObject;
            Actor = actor;
            TargetBox = targetBox;
            User = user;
        }

        public abstract Task<bool> Handle();
    }
}
