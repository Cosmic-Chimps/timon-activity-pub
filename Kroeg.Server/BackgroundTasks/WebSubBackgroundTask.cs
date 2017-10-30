using System;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.OStatusCompat;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Services;
using System.Collections.Generic;
using System.Data;
using Dapper;
using System.Data.Common;

namespace Kroeg.Server.BackgroundTasks
{
    public class WebSubBackgroundData
    {
        public bool Unsubscribe { get; set; }
        public string ActorID { get; set; }
        public int ToFollowID { get; set; }
    }

    public class WebSubBackgroundTask : BaseTask<WebSubBackgroundData, WebSubBackgroundTask>
    {
        private readonly IEntityStore _entityStore;
        private readonly DbConnection _connection;
        private readonly CollectionTools _collectionTools;

        public WebSubBackgroundTask(EventQueueItem item, IEntityStore entityStore, DbConnection connection, CollectionTools collectionTools) : base(item)
        {
            _entityStore = entityStore;
            _connection = connection;
            _collectionTools = collectionTools;
        }

        public override async Task Go()
        {
            var targetActor = await _entityStore.GetDBEntity(Data.ToFollowID);
            var actor = await _entityStore.GetEntity(Data.ActorID, true);

            if (targetActor == null || actor == null) return;

            var hubUrl = (string) targetActor.Data["_:hubUrl"].FirstOrDefault()?.Primitive;
            var topicUrl = (string)targetActor.Data["atomUri"].FirstOrDefault()?.Primitive;
            if (hubUrl == null || topicUrl == null) return;

            var clientObject = await _connection.QuerySingleOrDefaultAsync<WebSubClient>("select * from WebSubClients where ForUserId = @ForUserId and TargetUserId = @TargetUserId", new { ForUserID = actor.DbId, TargetUserId = targetActor.DbId});
            var hc = new HttpClient();
            if (Data.Unsubscribe)
            {
                if (clientObject.Expiry > DateTime.Now.AddMinutes(1))
                {
                    // send request
                    var content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["hub.mode"] = "unsubscribe",
                        ["hub.topic"] = topicUrl,
                        ["hub.secret"] = clientObject.Secret
                    });

                    await hc.PostAsync(hubUrl, content);

                }

                await _connection.ExecuteAsync("delete from \"WebSubClients\" where \"WebSubClientId\" = @Id", new { Id = clientObject.WebSubClientId });
                return;
            }

            if (clientObject == null)
            {
                clientObject = new WebSubClient
                {
                    ForUserId = actor.DbId,
                    TargetUserId = targetActor.DbId,
                    Secret = Guid.NewGuid().ToString(),
                    Topic = clientObject.Topic
                };

                await _connection.ExecuteAsync("insert into \"WebSubClients\" (\"ForUserId\", \"TargetUserId\", \"Secret\", \"Topic\") VALUES (@ForUserId, @TargetUserId, @Secret, @Topic", clientObject);
            }

            var subscribeContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["hub.mode"] = "subscribe",
                ["hub.topic"] = topicUrl,
                ["hub.secret"] = clientObject.Secret,
                ["hub.callback"] = (actor.Data["inbox"].First().Id) + ".atom",
                ["hub.lease_seconds"] = TimeSpan.FromDays(1).TotalSeconds.ToString()
            });

            var response = await hc.PostAsync(hubUrl, subscribeContent);
            var respText = await response.Content.ReadAsStringAsync();
            if (((int)response.StatusCode) / 100 != 2) response.EnsureSuccessStatusCode();
        }
    }
}
