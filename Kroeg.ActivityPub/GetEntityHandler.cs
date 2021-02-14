using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.EntityStore.Store;
using Kroeg.Server.Configuration;
using Kroeg.Services;
using Kroeg.EntityStore.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Npgsql;
using System.Net.WebSockets;
using System.Text;
using Kroeg.ActivityPub.Shared;
using Kroeg.EntityStore.Services;
using Kroeg.EntityStore.Notifier;
using Kroeg.ActivityPub.Services;

namespace Kroeg.ActivityPub
{
  public class GetEntityHandler
  {
    public readonly DbConnection _connection;
    private readonly EntityFlattener _flattener;
    private readonly IEntityStore _mainStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly DeliveryService _deliveryService;
    private readonly ClaimsPrincipal _user;
    private readonly CollectionTools _collectionTools;
    private readonly INotifier _notifier;
    private readonly JwtTokenSettings _tokenSettings;
    private readonly SignatureVerifier _verifier;
    private readonly IAuthorizer _authorizer;

    public GetEntityHandler(DbConnection connection, EntityFlattener flattener, IEntityStore mainStore,
        IServiceProvider serviceProvider, DeliveryService deliveryService,
        ClaimsPrincipal user, CollectionTools collectionTools, INotifier notifier, JwtTokenSettings tokenSettings,
        SignatureVerifier verifier, IAuthorizer authorizer)
    {
      _connection = connection;
      _flattener = flattener;
      _mainStore = mainStore;
      _serviceProvider = serviceProvider;
      _deliveryService = deliveryService;
      _user = user;
      _collectionTools = collectionTools;
      _notifier = notifier;
      _tokenSettings = tokenSettings;
      _verifier = verifier;
      _authorizer = authorizer;
    }

    public async Task<APEntity> Get(string url, IQueryCollection arguments, HttpContext context, APEntity existing)
    {
      var userId = _user.FindFirstValue("actor");
      var entity = existing ?? await _mainStore.GetEntity(url, false);
      if (entity == null) return null;
      if (userId == null) userId = await _verifier.Verify(url, context);
      if (!_authorizer.VerifyAccess(entity, userId))
      {
        var unauth = new ASObject();
        unauth.Id = "kroeg:unauthorized";
        unauth.Type.Add("kroeg:Unauthorized");

        return APEntity.From(unauth);
      }
      if (entity.Type == "https://www.w3.org/ns/activitystreams#OrderedCollection"
          || entity.Type == "https://www.w3.org/ns/activitystreams#Collection"
          || entity.Type.StartsWith("_"))
      {
        if (entity.IsOwner && !entity.Data["totalItems"].Any())
          try
          {
            return APEntity.From(await _getCollection(entity, arguments), true);
          }
          catch (FormatException)
          {
            throw new InvalidOperationException("Invalid parameters!");
          }
        else
          return (await _mainStore.GetEntity(url + context.Request.QueryString.Value, true)) ?? entity;
      }

      return entity;
    }

    public async Task EventStream(HttpContext context, string fullpath)
    {
      var entity = await _mainStore.GetEntity(fullpath, false);
      var authToken = context.Request.Query["authorization"].Concat(context.Request.Headers["Authorization"].Select(a => a.Split(' ')[1])).ToList();
      if (authToken.Count == 0)
      {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync("No authorization token provided");
        return;
      }

      var tokenHandler = new JwtSecurityTokenHandler();
      var claims = tokenHandler.ValidateToken(authToken[0], _tokenSettings.ValidationParameters, out Microsoft.IdentityModel.Tokens.SecurityToken validatedToken);
      var entityClaim = claims.FindFirstValue("actor");
      if (entityClaim == null) return;
      if (entity.Data["attributedTo"].TrueForAll(a => a.Id != entityClaim))
      {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync("Not authorized to view this item live");
        return;
      }

      if ((!entity.Type.StartsWith("_") && entity.Type != "OrderedCollection") || !entity.IsOwner)
      {
        context.Response.StatusCode = 415;
        await context.Response.WriteAsync("Cannot view this item live!");
        return;
      }


      context.Response.ContentType = "text/event-stream";
      context.Response.Headers.Add("X-Accel-Buffering", "no");
      var tokenSource = new CancellationTokenSource();
      ConcurrentQueue<string> toSend = new ConcurrentQueue<string>();

      Action<string> subscriptionCall = (item) =>
      {
        toSend.Enqueue(item);
        tokenSource.Cancel();
      };

      await _notifier.Subscribe($"collection/{fullpath}", subscriptionCall);
      context.RequestAborted.Register(() => tokenSource.Cancel());

      await context.Response.WriteAsync(": hello, world!\n");
      await context.Response.Body.FlushAsync();

      // todo: Last-Event-Id

      while (true)
      {
        if (toSend.Count == 0)
        {
          await context.Response.WriteAsync(":keepalive\n");
          await context.Response.Body.FlushAsync();
        }
        else
        {
          do
          {
            var success = toSend.TryDequeue(out var item);
            if (success)
            {
              var stored = await _mainStore.GetEntity(item, false);
              var unflattened = await _flattener.Unflatten(_mainStore, stored);
              var serialized = unflattened.Serialize(true).ToString(Formatting.None);
              await context.Response.WriteAsync($"id: {item}\ndata: {serialized}\n\n");
              await context.Response.Body.FlushAsync();
            }
          } while (toSend.Count > 0);
        }
        try
        {
          await ((NpgsqlConnection)_connection).WaitAsync(tokenSource.Token);
        }
        catch (OperationCanceledException)
        {
          if (context.RequestAborted.IsCancellationRequested) break;
        }

        tokenSource.Dispose();
        tokenSource = new CancellationTokenSource();
      }

      await _notifier.Unsubscribe($"collection/{fullpath}", subscriptionCall);
      context.Response.Body.Dispose();
    }

    public async Task WebSocket(HttpContext context, string fullpath)
    {

      var entity = await _mainStore.GetEntity(fullpath, false);
      var authToken = context.Request.Query["authorization"].Concat(context.Request.Headers["Authorization"].Select(a => a.Split(' ')[1])).ToList();
      if (authToken.Count == 0)
      {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync("No authorization token provided");
        return;
      }

      var tokenHandler = new JwtSecurityTokenHandler();
      var claims = tokenHandler.ValidateToken(authToken[0], _tokenSettings.ValidationParameters, out SecurityToken validatedToken);
      var entityClaim = claims.FindFirstValue("actor");
      if (entityClaim == null) return;
      if (entity.Data["attributedTo"].TrueForAll(a => a.Id != entityClaim))
      {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync("Not authorized to view this item live");
        return;
      }

      if ((!entity.Type.StartsWith("_") && entity.Type != "OrderedCollection") || !entity.IsOwner)
      {
        context.Response.StatusCode = 415;
        await context.Response.WriteAsync("Cannot view this item live!");
        return;
      }

      if (context.WebSockets.WebSocketRequestedProtocols.Count > 0 && !context.WebSockets.WebSocketRequestedProtocols.Contains("activitypub"))
      {
        context.Response.StatusCode = 415;
        await context.Response.WriteAsync("Invalid protocol?");
        return;
      }

      var tokenSource = new CancellationTokenSource();
      ConcurrentQueue<string> toSend = new ConcurrentQueue<string>();

      Action<string> subscriptionCall = (item) =>
      {
        toSend.Enqueue(item);
        tokenSource.Cancel();
      };

      await _notifier.Subscribe($"collection/{fullpath}", subscriptionCall);
      context.RequestAborted.Register(() => tokenSource.Cancel());

      WebSocket socket;
      if (context.WebSockets.WebSocketRequestedProtocols.Count > 0)
        socket = await context.WebSockets.AcceptWebSocketAsync("activitypub");
      else
        socket = await context.WebSockets.AcceptWebSocketAsync();
      var buf = new byte[1024];
      var seg = new ArraySegment<byte>(buf);

      while (true)
      {
        try
        {
          tokenSource.CancelAfter(30000);
          await ((NpgsqlConnection)_connection).WaitAsync(tokenSource.Token);
          var b = new ArraySegment<byte>(new byte[] { });
          await socket.SendAsync(b, WebSocketMessageType.Text, false, CancellationToken.None);
        }
        catch (TaskCanceledException) { }

        if (socket.State != WebSocketState.Open) break;

        do
        {
          var success = toSend.TryDequeue(out var item);
          if (success)
          {
            var stored = await _mainStore.GetEntity(item, false);

            var unflattened = await _flattener.Unflatten(_mainStore, stored);
            var serialized = Encoding.UTF8.GetBytes(unflattened.Serialize().ToString(Formatting.None));
            var segment = new ArraySegment<byte>(serialized);
            await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
          }
        } while (toSend.Count > 0);

        tokenSource.Dispose();
        tokenSource = new CancellationTokenSource();
      }

      await socket.CloseAsync(WebSocketCloseStatus.Empty, "other side closed", CancellationToken.None);
      await _notifier.Unsubscribe($"collection/{fullpath}", subscriptionCall);
    }

    private async Task<ASObject> _getCollection(APEntity entity, IQueryCollection arguments)
    {
      var from_id = arguments["from_id"].FirstOrDefault();
      var to_id = arguments["to_id"].FirstOrDefault();
      var collection = entity.Data;
      bool seePrivate = collection["attributedTo"].Any() && _user.FindFirstValue("actor") == collection["attributedTo"].First().Id;

      if (from_id != null || to_id != null)
      {
        var fromId = from_id != null ? int.Parse(from_id) : int.MaxValue;
        var toId = to_id != null ? int.Parse(to_id) : int.MinValue;
        var maxitem = (await _collectionTools.GetItems(entity.Id, count: 1)).FirstOrDefault()?.CollectionItemId;
        var items = await _collectionTools.GetItems(entity.Id, fromId, toId, count: 11);
        var hasItems = items.Any();
        var page = new ASObject();
        page.Type.Add("https://www.w3.org/ns/activitystreams#OrderedCollectionPage");
        page["summary"].Add(ASTerm.MakePrimitive("A collection"));
        page.Id = entity.Id + "?from_id=" + (hasItems ? fromId : 0);
        page["partOf"].Add(ASTerm.MakeId(entity.Id));
        if (collection["attributedTo"].Any())
          page["attributedTo"].Add(collection["attributedTo"].First());
        if (items.Count > 0 && items[0].CollectionItemId != maxitem)
          page["prev"].Add(ASTerm.MakeId(entity.Id + "?to_id=" + items[0].CollectionItemId.ToString()));
        if (items.Count > 10)
          page["next"].Add(ASTerm.MakeId(entity.Id + "?from_id=" + (items[9].CollectionItemId - 1).ToString()));
        page["orderedItems"].AddRange(items.Take(10).Select(a => ASTerm.MakeId(a.Entity.Id)));

        return page;
      }
      else
      {
        var items = await _collectionTools.GetItems(entity.Id, count: 1);
        var hasItems = items.Any();
        var page = entity.Id + "?from_id=" + (hasItems ? items.First().CollectionItemId + 1 : 0);
        collection["current"].Add(ASTerm.MakeId(entity.Id));
        collection["totalItems"].Add(ASTerm.MakePrimitive(await _collectionTools.Count(entity.Id), ASTerm.NON_NEGATIVE_INTEGER));
        collection["first"].Add(ASTerm.MakeId(page));
        return collection;
      }
    }

    public async Task<APEntity> Post(HttpContext context, string fullpath, APEntity original, ASObject @object)
    {
      if (!original.IsOwner) return null;

      switch (original.Type)
      {
        case "_inbox":
          var actorObj = @object["actor"].First();
          string subjectId = actorObj.Id ?? actorObj.SubObject.Id;
          subjectId = await _verifier.Verify(fullpath, context) ?? subjectId;

          if (subjectId == null)
          {
            throw new UnauthorizedAccessException("Invalid signature");
          }
          return await ServerToServer(original, @object, subjectId);
        case "_outbox":
          var userId = original.Data["attributedTo"].FirstOrDefault() ?? original.Data["actor"].FirstOrDefault();
          if (userId == null || _user.FindFirst("actor").Value == userId.Id)
          {
            return await ClientToServer(original, @object);
          }
          throw new UnauthorizedAccessException("Cannot post to the outbox of another actor");
      }

      return null;
    }

    public async Task<APEntity> ServerToServer(APEntity inbox, ASObject activity, string subject = null)
    {
      var stagingStore = new StagingEntityStore(_mainStore);
      var userId = inbox.Data["attributedTo"].Single().Id;
      var user = await _mainStore.GetEntity(userId, false);

      APEntity flattened;

      string prefix = "";
      if (subject != null)
      {
        var subjectUri = new Uri(subject);
        prefix = $"{subjectUri.Scheme}://{subjectUri.Host}";
        if (!subjectUri.IsDefaultPort) prefix += $":{subjectUri.Port}";
        prefix += "/";
      }

      var id = activity.Id;
      flattened = await _mainStore.GetEntity(id, false);
      if (flattened == null)
        flattened = await _flattener.FlattenAndStore(stagingStore, activity, false);

      stagingStore.TrimDown(prefix); // remove all staging entities that may be faked

      var sentBy = activity["actor"].First().Id;
      if (subject != null && sentBy != subject)
        throw new UnauthorizedAccessException("Invalid authorization header for this subject!");

      if (user.Data["blocks"].Any())
      {
        var blocks = await _mainStore.GetEntity(user.Data["blocks"].First().Id, false);
        var blocked = await _mainStore.GetEntity(blocks.Data["blocked"].First().Id, false);
        if (await _collectionTools.Contains(blocked, sentBy))
          throw new UnauthorizedAccessException("You are blocked.");
      }

      if (await _collectionTools.Contains(inbox, id))
        return flattened;

      await stagingStore.CommitChanges();

      foreach (var type in ServerConfig.ServerToServerHandlers)
      {
        var handler = (BaseHandler)ActivatorUtilities.CreateInstance(_serviceProvider, type,
            _mainStore, flattened, user, inbox, _user);
        var handled = await handler.Handle();
        flattened = handler.MainObject;
        if (!handled) break;
      }

      return flattened;
    }

    public async Task<APEntity> ClientToServer(APEntity outbox, ASObject activity)
    {
      var stagingStore = new StagingEntityStore(_mainStore);
      var userId = outbox.Data["attributedTo"].Single().Id;
      var user = await _mainStore.GetEntity(userId, false);

      if (!EntityData.IsActivity(activity))
      {
#pragma warning disable CS0618 // Type or member is obsolete
        if (EntityData.IsActivity(activity.Type.FirstOrDefault()))
#pragma warning restore CS0618 // Type or member is obsolete
        {
          throw new UnauthorizedAccessException("Sending an Activity without an actor isn't allowed. Are you sure you wanted to do that?");
        }

        var createActivity = new ASObject();
        createActivity.Type.Add("https://www.w3.org/ns/activitystreams#Create");
        createActivity["to"].AddRange(activity["to"]);
        createActivity["bto"].AddRange(activity["bto"]);
        createActivity["cc"].AddRange(activity["cc"]);
        createActivity["bcc"].AddRange(activity["bcc"]);
        createActivity["audience"].AddRange(activity["audience"]);
        createActivity["actor"].Add(ASTerm.MakeId(userId));
        createActivity["object"].Add(ASTerm.MakeSubObject(activity));
        activity = createActivity;
      }

      if (activity.Type.Contains("https://www.w3.org/ns/activitystreams#Create"))
      {
        activity.Id = null;
        if (activity["object"].SingleOrDefault()?.SubObject != null)
          activity["object"].Single().SubObject.Id = null;
      }

      var flattened = await _flattener.FlattenAndStore(stagingStore, activity);
      IEntityStore store = stagingStore;

      foreach (var type in ServerConfig.ClientToServerHandlers)
      {
        var handler = (BaseHandler)ActivatorUtilities.CreateInstance(_serviceProvider, type,
            store, flattened, user, outbox, _user);
        var handled = await handler.Handle();
        flattened = handler.MainObject;
        if (!handled) break;
        if (type == typeof(CommitChangesHandler))
          store = _mainStore;
      }

      return flattened;
    }
  }
}
