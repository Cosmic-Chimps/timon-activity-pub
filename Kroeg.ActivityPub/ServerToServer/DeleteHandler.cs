using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Store;
using Kroeg.Services;

namespace Kroeg.ActivityPub.ServerToServer
{
    public class DeleteHandler : BaseHandler
    {
        private static readonly HashSet<string> DeleteWhitelist = new HashSet<string> { "id", "type", "created", "updated" };

        public DeleteHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user) : base(entityStore, mainObject, actor, targetBox, user)
        {
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "https://www.w3.org/ns/activitystreams#Delete") return true;

            var oldObject = await EntityStore.GetEntity(MainObject.Data["object"].Single().Id, true);
            var newData = oldObject.Data;
            foreach (var kv in newData)
            {
                if (!DeleteWhitelist.Contains(kv.Key))
                    kv.Value.Clear();
            }

            newData.Type.Clear();
            newData.Type.Add("https://www.w3.org/ns/activitystreams#Tombstone");
            newData.Replace("deleted", ASTerm.MakePrimitive(DateTime.Now.ToString("o")));

            var newObject = APEntity.From(newData);
            await EntityStore.StoreEntity(newObject);

            return true;
        }
    }
}
