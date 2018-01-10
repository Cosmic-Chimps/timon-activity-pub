using System;
using System.Threading.Tasks;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Store;
using Kroeg.EntityStore;

namespace Kroeg.ActivityPub.BackgroundTasks
{
    public class GetEntityTask : BaseTask<string, GetEntityTask>
    {
        private readonly IEntityStore _entityStore;

        public GetEntityTask(EventQueueItem item, IEntityStore entityStore) : base(item)
        {
            _entityStore = entityStore;
        }

        public override async Task Go()
        {
            try {
                await _entityStore.GetEntity(Data, true);
            } catch (Exception) { /* nom */ }
        }
    }
}
