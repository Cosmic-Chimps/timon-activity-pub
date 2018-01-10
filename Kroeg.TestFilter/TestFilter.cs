using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.Services;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Store;


namespace Kroeg.TestFilter
{
    public class SpamFilter : BaseHandler
    {
        public SpamFilter(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user) : base(entityStore, mainObject, actor, targetBox, user)
        {
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "https://www.w3.org/ns/activitystreams#Create") return true;
            var subObject = await EntityStore.GetEntity(MainObject.Data["object"].Single().Id, true);

            if (subObject.Data["content"].Any(a => (a.Primitive as string)?.Contains("spam") == true)) throw new InvalidOperationException("Sorry, \"\"\"spam\"\"\" filter!");
            return true;
        }
    }
}