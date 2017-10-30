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
using System.Data;
using Dapper;
using System.Data.Common;

namespace Kroeg.Server.BackgroundTasks
{
    public class DeliverToWebSubData
    {
        public int Subscription { get; set; }
        public string SourceUserId { get; set; }
        public string ObjectId { get; set; }
        public int CollectionItem { get; set; }
    }

    public class DeliverToWebSubTask : BaseTask<DeliverToWebSubData, DeliverToWebSubTask>
    {
        private readonly IEntityStore _entityStore;
        private WebsubSubscription _subscription;
        private readonly AtomEntryGenerator _entryGenerator;
        private readonly DbConnection _connection;

        private async Task<ASObject> _object()
        {
            var user = (await _entityStore.GetEntity(Data.SourceUserId, false)).Data;
            var outboxId = user["outbox"].First().Id;

            var page = new ASObject();
            page.Type.Add("https://www.w3.org/ns/activitystreams#OrderedCollectionPage");
            page.Id = outboxId + "?from_id=" + (Data.CollectionItem + 1);
            page["attributedTo"].Add(ASTerm.MakeId(Data.SourceUserId));
            page["orderedItems"].Add(ASTerm.MakeId(Data.ObjectId));
            return page;
        }

        private string _calculateSignature(byte[] data)
        {
            // so Mastodon requires sha1 on WebSub. this is horrible. How did Mastodon ever get popular

            var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_subscription.Secret));
            var bytestring = BitConverter.ToString(hmac.ComputeHash(data)).Replace("-", "").ToLower();
            return $"sha1={bytestring}";
        }

        public DeliverToWebSubTask(EventQueueItem item, IEntityStore entityStore, AtomEntryGenerator entryGenerator, DbConnection connection) : base(item)
        {
            _entityStore = entityStore;
            _entryGenerator = entryGenerator;
            _connection = connection;
        }

        public override async Task Go()
        {
            _subscription = await _connection.QuerySingleOrDefaultAsync<WebsubSubscription>("select * from WebsubSubscriptions where Id = @Id", new { Id = Data.Subscription });
            if (_subscription == null)
                return; // gone
            var objdata = await _object();
            // building fake feed because it's wanted

            var hc = new HttpClient();
            var serialized = Encoding.UTF8.GetBytes((await _entryGenerator.Build(objdata)).ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
            
            var content = new ByteArrayContent(serialized);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/atom+xml");
            var request = new HttpRequestMessage(HttpMethod.Post, _subscription.Callback) {Content = content};
            if (!string.IsNullOrWhiteSpace(_subscription.Secret))
            {
                var sig = _calculateSignature(serialized);
                request.Headers.TryAddWithoutValidation("X-Hub-Signature", sig);
            }

            var result = await hc.SendAsync(request);
            if (!result.IsSuccessStatusCode && (int)result.StatusCode / 100 == 5)
                throw new Exception("Failed to deliver. Retrying later.");
        }
    }
}
