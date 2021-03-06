using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Store;
using Kroeg.Services;
using Kroeg.EntityStore.Services;

namespace Kroeg.ActivityPub.ClientToServer
{
    public class UpdateDeleteActivityHandler : BaseHandler
    {
        private readonly RelevantEntitiesService _relevantEntities;
        private static readonly HashSet<string> UpdateBlacklist = new HashSet<string> { "attributedTo", "published", "id", "deleted", "type", "object", "actor" };
        private static readonly HashSet<string> DeleteWhitelist = new HashSet<string> { "id", "type", "published", "updated" };

        public UpdateDeleteActivityHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, RelevantEntitiesService relevantEntities) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _relevantEntities = relevantEntities;
        }

        private static bool _isEqual(ASTerm a, ASTerm b)
        {
            return (a.Id == b.Id) || (a.Primitive.GetType() == b.Primitive.GetType() && (a.Primitive is string
                       ? (string) a.Primitive == (string) b.Primitive
                       : a.Primitive == b.Primitive));
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "https://www.w3.org/ns/activitystreams#Update" && MainObject.Type != "https://www.w3.org/ns/activitystreams#Delete") return true;

            var activityData = MainObject.Data;

            var oldObject =
                await EntityStore.Bypass.GetEntity(activityData["object"].Single().Id, false);

            if (oldObject == null)
                throw new InvalidOperationException("Cannot remove or update a non-existant object!");

            if (!oldObject.IsOwner) throw new InvalidOperationException("Cannot remove or update an object not on this server!");

            var originatingCreate = (await _relevantEntities.FindRelevantObject("https://www.w3.org/ns/activitystreams#Create", oldObject.Id)).FirstOrDefault();
            if (originatingCreate.Data["actor"].Single().Id != Actor.Id)
                throw new InvalidOperationException("Cannot remove or update objects that weren't made by you!");

            if (MainObject.Type == "https://www.w3.org/ns/activitystreams#Update")
            {
                var newObject = await EntityStore.GetEntity(activityData["object"].Single().Id, false);
                if (newObject == oldObject) throw new InvalidOperationException("No new object passed!");

                var data = oldObject.Data;
                foreach (var item in newObject.Data)
                {
                    // SequenceEqual ensures that clients doing full-object updates won't cause this exception on e.g. type, attributedTo, etc
                    if (UpdateBlacklist.Contains(item.Key) && (data[item.Key].Count != item.Value.Count || data[item.Key].Zip(item.Value, _isEqual).Any(a => !a)))
                        throw new InvalidOperationException($"Cannot update key {item.Key} as it's on the blacklist!");

                    data[item.Key].Clear();
                    data[item.Key].AddRange(item.Value);
                }

                data.Replace("updated", ASTerm.MakePrimitive(DateTime.Now.ToString("o")));

                newObject.Data = data;
                newObject.Type = oldObject.Type;
                await EntityStore.StoreEntity(newObject);
            }
            else
            {
                var newData = oldObject.Data;
                foreach (var kv in newData)
                {
                    if (!DeleteWhitelist.Contains(kv.Key))
                        kv.Value.Clear();
                }

                newData.Type.Clear();
                newData.Type.Add("https://www.w3.org/ns/activitystreams#Tombstone");
                newData.Replace("deleted", ASTerm.MakePrimitive(DateTime.Now.ToString("o")));

                var newObject = APEntity.From(newData, true);
                await EntityStore.StoreEntity(newObject);
            }

            return true;
        }
    }
}
