using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Kroeg.Server.Salmon;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class CreateActorHandler : BaseHandler
    {
        private readonly CollectionTools _collection;
        private readonly EntityData _entityData;
        private readonly APContext _context;
        public string UserOverride { get; set; }

        public CreateActorHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection, EntityData entityData, APContext context) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
            _entityData = entityData;
            _context = context;
        }

        private async Task<APEntity> AddCollection(ASObject entity, string obj, string parent)
        {
            var collection = await _collection.NewCollection(EntityStore, null, "_" + obj, parent);
            var data = collection.Data;
            data.Replace("attributedTo", ASTerm.MakeId(parent));
            collection.Data = data;

            await EntityStore.StoreEntity(collection);

            entity.Replace(obj, ASTerm.MakeId(collection.Id));
            return collection;
        }

        private void _merge(List<ASTerm> to, List<ASTerm> from)
        {
            var str = new HashSet<string>(to.Select(a => a.Id).Concat(from.Select(a => a.Id)));

            to.Clear();
            to.AddRange(str.Select(a => ASTerm.MakeId(a)));
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "Create") return true;

            var activityData = MainObject.Data;
            var objectEntity = await EntityStore.GetEntity((string) activityData["object"].First().Primitive, false);
            if (!_entityData.IsActor(objectEntity.Data)) return true;
            var objectData = objectEntity.Data;
            var id = objectEntity.Id;

            await AddCollection(objectData, "inbox", id);
            await AddCollection(objectData, "outbox", id);
            await AddCollection(objectData, "following", id);
            await AddCollection(objectData, "followers", id);
            await AddCollection(objectData, "liked", id);

            var blocks = await AddCollection(objectData, "blocks", id);
            var blocked = await _collection.NewCollection(EntityStore, null, "_blocked", blocks.Id);

            var blocksData = blocks.Data;
            blocksData["kroeg:_blocked"].Add(ASTerm.MakeId(blocked.Id));
            blocks.Data = blocksData;

            if (!objectData["manuallyApprovesFollowers"].Any())
                activityData.Replace("manuallyApprovesFollowers", ASTerm.MakePrimitive(false));

            objectEntity.Data = objectData;

            await EntityStore.StoreEntity(blocked);
            await EntityStore.StoreEntity(blocks);
            await EntityStore.StoreEntity(objectEntity);

            var userId = UserOverride ?? User.FindFirst(ClaimTypes.NameIdentifier).Value;
            _context.UserActorPermissions.Add(new UserActorPermission { UserId = userId, ActorId = objectEntity.Id, IsAdmin = true });

            var key = new SalmonKey();
            var salmon = MagicKey.Generate();
            key.EntityId = objectEntity.Id;
            key.PrivateKey = salmon.PrivateKey;

            _context.SalmonKeys.Add(key);

            if (!activityData["actor"].Any())
                activityData["actor"].Add(ASTerm.MakeId(objectEntity.Id));

            MainObject.Data = activityData;
            await EntityStore.StoreEntity(MainObject);

            await EntityStore.CommitChanges();
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
