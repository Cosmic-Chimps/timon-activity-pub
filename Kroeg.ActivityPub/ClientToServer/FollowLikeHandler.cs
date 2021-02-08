using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Store;
using Kroeg.Services;
using Kroeg.EntityStore.Services;

namespace Kroeg.ActivityPub.ClientToServer
{
  public class FollowLikeHandler : BaseHandler
  {
    private readonly CollectionTools _collection;

    public FollowLikeHandler(IEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection) : base(entityStore, mainObject, actor, targetBox, user)
    {
      _collection = collection;
    }

    public override async Task<bool> Handle()
    {
      if (MainObject.Type != "https://www.w3.org/ns/activitystreams#Like") return true;

      var userData = Actor.Data;
      string targetCollectionId = null;
      targetCollectionId = userData["liked"].SingleOrDefault()?.Id;

      if (targetCollectionId == null) return true;

      var targetCollection = await EntityStore.GetEntity(targetCollectionId, false);
      var objectEntity = await EntityStore.GetEntity(MainObject.Data["object"].Single().Id, true);
      if (objectEntity == null) throw new InvalidOperationException($"Cannot {MainObject.Type.ToLower()} a non-existant object!");

      await _collection.AddToCollection(targetCollection, objectEntity);
      return true;
    }
  }
}
