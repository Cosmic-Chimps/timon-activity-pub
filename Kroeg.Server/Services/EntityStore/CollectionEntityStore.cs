
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Configuration;
using Kroeg.Server.Models;
using Microsoft.AspNetCore.Http;

namespace Kroeg.Server.Services.EntityStore
{
    public class CollectionEntityStore : IEntityStore
    {
        private readonly CollectionTools _collectionTools;

        private async Task<ASObject> _buildPage(APEntity entity, int from_id)
        {
            var collection = entity.Data;
            var items = await _collectionTools.GetItems(entity.Id, from_id, 11);
            var hasItems = items.Any();
            var page = new ASObject();
            page.Type.Add("https://www.w3.org/ns/activitystreams#OrderedCollectionPage");
            page["summary"].Add(ASTerm.MakePrimitive("A collection"));
            page.Id = entity.Id + "?from_id=" + (hasItems ? from_id : 0);
            page["partOf"].Add(ASTerm.MakeId(entity.Id));
            if (collection["attributedTo"].Any())
                page["attributedTo"].Add(collection["attributedTo"].First());
            if (items.Count > 10)
                page["next"].Add(ASTerm.MakeId(entity.Id + "?from_id=" + (items[9].CollectionItemId - 1).ToString()));
            page["orderedItems"].AddRange(items.Take(10).Select(a => ASTerm.MakeId(a.Entity.Id)));
            return page;
        }

        private async Task<ASObject> _buildCollection(APEntity entity)
        {
            var collection = entity.Data;
            collection["current"].Add(ASTerm.MakeId(entity.Id));
            collection["totalItems"].Add(ASTerm.MakePrimitive(await _collectionTools.Count(entity.Id), ASTerm.NON_NEGATIVE_INTEGER));
            var item = await _collectionTools.GetItems(entity.Id, count: 1);
            if (item.Any())
                collection["first"].Add(ASTerm.MakeId(entity.Id + $"?from_id={item.First().CollectionItemId + 1}"));
            else
                collection["first"].Add(ASTerm.MakeId(entity.Id + $"?from_id=0"));
            return collection;
        }

        public CollectionEntityStore(CollectionTools collectionTools, IEntityStore next)
        {
            _collectionTools = collectionTools;
            Bypass = next;
        }

        public async Task<APEntity> GetEntity(string id, bool doRemote)
        {
            string query = null;
            string parsedId = null;

            if (id.Contains("?"))
            {
                var split = id.Split(new char[] { '?' }, 2);
                query = split[1];
                parsedId = split[0];
            }

            APEntity original;
            var entity = original = await Bypass.GetEntity(id, false);
            if (entity == null) entity = await Bypass.GetEntity(parsedId, false);
            if (entity == null || !entity.IsOwner) return doRemote ? await Bypass.GetEntity(id, true) : original;
            if (!entity.Type.StartsWith("_") && entity.Type != "OrderedCollection") return doRemote ? await Bypass.GetEntity(id, true) : original;

            if (query == null)
            {
                return APEntity.From(await _buildCollection(entity), true);
            }
            else
            {
                int from_id = 0;
                foreach (var item in query.Split('&'))
                {
                    var kv = item.Split('=');
                    if (kv[0] == "from_id" && kv.Length > 1)
                        from_id = int.Parse(kv[1]);
                }

                return APEntity.From(await _buildPage(entity, from_id));
            }
        }

        public Task<APEntity> StoreEntity(APEntity entity)
        {
            return Bypass.StoreEntity(entity);
        }

        public async Task CommitChanges()
        {
            await Bypass.CommitChanges();
        }
        
        public IEntityStore Bypass { get; }
    }
}