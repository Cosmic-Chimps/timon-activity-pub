using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Store;
using Kroeg.Services;

namespace Kroeg.ActivityPub.Shared
{
    public class CommitChangesHandler : BaseHandler
    {
        public CommitChangesHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user) : base(entityStore, mainObject, actor, targetBox, user)
        {
        }

        public override async Task<bool> Handle()
        {
            await EntityStore.CommitChanges();
            return true;
        }
    }
}
