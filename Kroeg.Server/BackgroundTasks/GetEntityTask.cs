using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Newtonsoft.Json;
using Kroeg.Server.Services;
using Kroeg.ActivityStreams;
using Kroeg.Server.Middleware;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Linq;
using Kroeg.Server.Salmon;

namespace Kroeg.Server.BackgroundTasks
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
