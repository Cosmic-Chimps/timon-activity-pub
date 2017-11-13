using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.BackgroundTasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Microsoft.IdentityModel.Tokens;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Kroeg.Server.Configuration;
using System.Data;
using Dapper;
using System.Data.Common;

namespace Kroeg.Server.Services
{
    public class DeliveryService
    {
        private readonly DbConnection _connection;
        private readonly EntityData _configuration;
        private readonly CollectionTools _collectionTools;
        private readonly IEntityStore _store;
        private readonly RelevantEntitiesService _relevantEntities;

        public DeliveryService(DbConnection connection, CollectionTools collectionTools, EntityData configuration, IEntityStore store, RelevantEntitiesService relevantEntities)
        {
            _connection = connection;
            _collectionTools = collectionTools;
            _configuration = configuration;
            _store = store;
            _relevantEntities = relevantEntities;
        }

        public static bool IsPublic(ASObject @object)
        {
            var targetIds = new List<string>();

            targetIds.AddRange(@object["to"].Select(a => a.Id));
            targetIds.AddRange(@object["bto"].Select(a => a.Id));

            return targetIds.Contains("https://www.w3.org/ns/activitystreams#Public");
        }

        public async Task QueueDeliveryForEntity(APEntity entity, int collectionId, string ownedBy = null)
        {
            var audienceInbox = await _buildAudienceInbox(entity.Data, forward: ownedBy, actor: false);
            // Is public post?
            if (audienceInbox.Item2 && ownedBy == null)
            {
                await _queueWebsubDelivery(entity.Data["actor"].First().Id, collectionId, entity.Id);
            }

            foreach (var target in audienceInbox.Item1)
                await _queueInboxDelivery(target, entity);

            foreach (var salmon in audienceInbox.Item3)
                await _queueSalmonDelivery(salmon, entity);
        }

        public async Task<List<APEntity>> GetUsersForSharedInbox(ASObject objectToProcess)
        {
            var audience = GetAudienceIds(objectToProcess);
            var result = new HashSet<string>();
            foreach (var entity in audience)
            {
                List<APEntity> followers = null;
                var data = await _store.GetEntity(entity, false);
                if (data != null && data.IsOwner && data.Type == "_followers")
                {
                    followers = new List<APEntity> { await _store.GetEntity((string)data.Data["attributedTo"].Single().Primitive, false) };
                }
                else if (data != null && !data.IsOwner)
                {
                    followers = await _relevantEntities.FindEntitiesWithFollowerId(data.Id);
                }

                if (followers == null || followers.Count == 0) continue; // apparently not a follower list? giving up.

                foreach (var f in followers)
                {
                    var following = await _collectionTools.CollectionsContaining(f.Id, "_following");
                    foreach (var item in following)
                    {
                        result.Add((string)item.Data["attributedTo"].Single().Primitive);
                    }
                }
            }

            var resultList = new List<APEntity>();
            foreach (var item in result)
                resultList.Add(await _store.GetEntity(item, false));

            return resultList;
        }

        public static HashSet<string> GetAudienceIds(ASObject @object)
        {
            var targetIds = new List<string>();

            targetIds.AddRange(@object["to"].Select(a => a.Id));
            targetIds.AddRange(@object["bto"].Select(a => a.Id));
            targetIds.AddRange(@object["cc"].Select(a => a.Id));
            targetIds.AddRange(@object["bcc"].Select(a => a.Id));
            targetIds.AddRange(@object["audience"].Select(a => a.Id));
            targetIds.AddRange(@object["attributedTo"].Select(a => a.Id));
            targetIds.AddRange(@object["actor"].Select(a => a.Id));

            return new HashSet<string>(targetIds);
        }

        private async Task<Tuple<HashSet<string>, bool, HashSet<string>>> _buildAudienceInbox(ASObject @object, int depth = 3, string forward = null, bool actor = true)
        {
            var targetIds = new List<string>();

            targetIds.AddRange(@object["to"].Select(a => a.Id));
            targetIds.AddRange(@object["bto"].Select(a => a.Id));
            targetIds.AddRange(@object["cc"].Select(a => a.Id));
            targetIds.AddRange(@object["bcc"].Select(a => a.Id));
            targetIds.AddRange(@object["audience"].Select(a => a.Id));

            if (!actor) targetIds.Remove(@object["actor"].First().Id);

            bool isPublic = targetIds.Contains("https://www.w3.org/ns/activitystreams#Public");
            targetIds.Remove("https://www.w3.org/ns/activitystreams#Public");

            var targets = new HashSet<string>();
            var stack = new Stack<Tuple<int, APEntity, bool>>();
            var salmons = new HashSet<string>();
            foreach (var item in targetIds)
            {
                var entity = await _store.GetEntity(item, true);
                var data = entity.Data;
                // if it's local collection, or we don't need the forwarding thing
                var iscollection = data.Type.Contains("https://www.w3.org/ns/activitystreams#Collection") || data.Type.Contains("https://www.w3.org/ns/activitystreams#OrderedCollection");
                var shouldForward = entity.IsOwner && (forward == null || data["attributedTo"].Any(a => a.Id == forward));
                if (!iscollection || shouldForward)
                    stack.Push(new Tuple<int, APEntity, bool>(0, entity, false));
            }

            while (stack.Any())
            {
                var entity = stack.Pop();

                var data = entity.Item2.Data;
                var iscollection = data.Type.Contains("https://www.w3.org/ns/activitystreams#Collection") || data.Type.Contains("https://www.w3.org/ns/activitystreams#OrderedCollection");
                var shouldForward = entity.Item2.IsOwner && (forward == null || data["attributedTo"].Any(a => a.Id == forward));
                var useSharedInbox = (entity.Item2.IsOwner && entity.Item2.Type == "_following");
                if ((iscollection && shouldForward) && entity.Item1 < depth)
                {
                    foreach (var item in await _collectionTools.GetAll(entity.Item2.Id))
                        stack.Push(new Tuple<int, APEntity, bool>(entity.Item1 + 1, item.Entity, useSharedInbox));
                }
                else if (forward == null && _configuration.IsActor(data))
                {
                    if (entity.Item3)
                    {
                        var endpoints = data["endpoints"].First().SubObject ?? (await _store.GetEntity(data["endpoints"].First().Id, false)).Data;
                        if (endpoints["sharedInbox"].Any())
                            targets.Add(endpoints["sharedInbox"].First().Id);
                        continue;
                    }

                    if (data["inbox"].Any())
                        targets.Add(data["inbox"].First().Id);
                    else if (data["_:salmonUrl"].Any())
                        salmons.Add((string)data["_:salmonUrl"].First().Primitive);
                }
            }

            return new Tuple<HashSet<string>, bool, HashSet<string>>(targets, isPublic, salmons);
        }

        private async Task _queueInboxDelivery(string targetUrl, APEntity entity)
        {
            await DeliverToActivityPubTask.Make(new DeliverToActivityPubData
                {
                    ObjectId = entity.Id,
                    TargetInbox = targetUrl
                }, _connection);
        }

        private async Task _queueSalmonDelivery(string targetUrl, APEntity entity)
        {
           await DeliverToSalmonTask.Make(new DeliverToSalmonData
                {
                    EntityId = entity.Id,
                    SalmonUrl = targetUrl
                }, _connection);
        }

        private async Task _queueWebsubDelivery(string userId, int collectionItem, string objectId)
        {
            var actor = await _store.GetEntity(userId, false);

            foreach (var sub in await (_connection.QueryAsync<WebsubSubscription>("select * from \"WebsubSubscriptions\" where \"UserId\" = @UserId and \"Expiry\" > @Expiry", new { UserId = actor.DbId, Expiry = DateTime.Now })))
            {
                await DeliverToWebSubTask.Make(new DeliverToWebSubData
                    {
                        CollectionItem = collectionItem,
                        ObjectId = objectId,
                        SourceUserId = userId,
                        Subscription = sub.Id
                    }, _connection);
            }
        }
    }
}
