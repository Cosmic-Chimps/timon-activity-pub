using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityPub.Services;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Services;
using Kroeg.Services;

namespace Kroeg.ActivityPub
{
    public class DefaultAuthorizer : IAuthorizer
    {
        private readonly ServerConfig _entityData;

        public DefaultAuthorizer(ServerConfig entityData)
        {
            _entityData = entityData;
        }

        public async Task<bool> VerifyAccess(APEntity entity, string userId)
        {
            if (entity.Type == "_blocks" && !entity.Data["attributedTo"].Any(a => a.Id == userId)) return false;
            if (entity.Type == "_blocked") return false;
            if (entity.Type == "https://www.w3.org/ns/activitystreams#OrderedCollection" || entity.Type == "https://www.w3.org/ns/activitystreams#Collection" || entity.Type.StartsWith("_")) return true;
            if (EntityData.IsActor(entity.Data)) return true;

            var audience = DeliveryService.GetAudienceIds(entity.Data);
            return (
                entity.Data["attributedTo"].Concat(entity.Data["actor"]).Any(a => a.Id == userId)
                || audience.Contains("https://www.w3.org/ns/activitystreams#Public")
                || (userId != null  && audience.Contains(userId))
                );
        }
    }
}