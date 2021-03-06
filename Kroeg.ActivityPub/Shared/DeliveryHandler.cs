using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Store;
using Kroeg.Services;
using Kroeg.EntityStore.Services;
using Kroeg.ActivityPub.Services;

namespace Kroeg.ActivityPub.Shared
{
    public class DeliveryHandler : BaseHandler
    {
        private readonly CollectionTools _collection;
        private readonly DeliveryService _deliveryService;

        public DeliveryHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection, DeliveryService deliveryService) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
            _deliveryService = deliveryService;
        }

        public override async Task<bool> Handle()
        {
            if (!await _collection.Contains(TargetBox, MainObject.Id))
            {
                var addedTo = await _collection.AddToCollection(TargetBox, MainObject);
                if (MainObject.Type == "https://www.w3.org/ns/activitystreams#Block") return true;
                await _deliveryService.QueueDeliveryForEntity(MainObject, addedTo.CollectionItemId, TargetBox.Type == "_inbox" ? Actor.Id : null);
            }

            return true;
        }
    }
}
