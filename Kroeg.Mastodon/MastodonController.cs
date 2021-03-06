using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.Server.Configuration;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Store;
using Microsoft.AspNetCore.Mvc;
using Kroeg.EntityStore.Services;

namespace Kroeg.Mastodon
{
  [Route("/api/v1/")]
  public class MastodonController : Controller
  {
    private readonly IEntityStore _entityStore;
    private readonly RelevantEntitiesService _relevantEntities;
    private readonly CollectionTools _collectionTools;

    public MastodonController(IEntityStore entityStore, RelevantEntitiesService relevantEntities, CollectionTools collectionTools)
    {
      _entityStore = entityStore;
      _relevantEntities = relevantEntities;
      _collectionTools = collectionTools;
    }

    private async Task<Account> _processAccount(APEntity entity)
    {
      var result = new Account
      {
        id = Uri.EscapeDataString(entity.Id),
        username = (string)entity.Data["preferredUsername"].FirstOrDefault()?.Primitive ?? entity.Id,
        display_name = (string)entity.Data["name"].FirstOrDefault()?.Primitive ?? entity.Id,
        locked = entity.Data["manuallyApprovesFollowers"].Any(a => (bool)a.Primitive),
        created_at = DateTime.Now,
        note = (string)entity.Data["summary"].FirstOrDefault()?.Primitive ?? "",
        url = (string)entity.Data["url"].FirstOrDefault()?.Primitive ?? entity.Id,
        moved = null,

        followers_count = -1,
        following_count = -1,
        statuses_count = -1,

        avatar = "",
        avatar_static = ""
      };

      if (entity.IsOwner) result.acct = result.username;
      else
        result.acct = result.username + "@" + (new Uri(entity.Id)).Host;

      if (entity.Data["icon"].Any())
        result.avatar = result.avatar_static = entity.Data["icon"].First().Id ?? entity.Data["icon"].First().SubObject["url"].First().Id;

      var followers = await _entityStore.GetEntity(entity.Data["followers"].First().Id, false);
      if (followers != null && followers.Data["totalItems"].Any())
        result.followers_count = (int)followers.Data["totalItems"].First().Primitive;

      var following = await _entityStore.GetEntity(entity.Data["following"].First().Id, false);
      if (following != null && following.Data["totalItems"].Any())
        result.following_count = (int)following.Data["totalItems"].First().Primitive;

      var outbox = await _entityStore.GetEntity(entity.Data["outbox"].First().Id, false);
      if (outbox != null && outbox.Data["totalItems"].Any())
        result.statuses_count = (int)outbox.Data["totalItems"].First().Primitive;

      return result;
    }

    private async Task<Status> _translateNote(APEntity note, string id)
    {
      if (note == null) return null;
      if (note.Type != "https://www.w3.org/ns/activitystreams#Note") return null;

      var attributed = await _entityStore.GetEntity(note.Data["attributedTo"].First().Id, true);

      var status = new Status
      {
        id = id ?? Uri.EscapeDataString(note.Id),
        uri = note.Id,
        url = note.Data["url"].FirstOrDefault()?.Id ?? note.Id,
        account = await _processAccount(attributed),
        in_reply_to_id = note.Data["inReplyTo"].Any() ? Uri.EscapeDataString(note.Data["inReplyTo"].FirstOrDefault()?.Id) : null,
        reblog = null,
        content = (string)note.Data["content"].First().Primitive,
        created_at = DateTime.Parse((string)note.Data["published"].FirstOrDefault()?.Primitive ?? note.Updated.ToString()),
        emojis = new List<Emoji>(),
        reblogs_count = 0,
        favourites_count = 0,
        reblogged = false,
        favourited = false,
        muted = false,
        sensitive = note.Data["sensitive"].Any(a => (bool)a.Primitive),
        spoiler_text = (string)note.Data["summary"].FirstOrDefault()?.Primitive ?? "",
        visibility = note.Data["to"].Any(a => a.Id == "https://www.w3.org/ns/activitystreams#Public") ? "public"
                      : note.Data["cc"].Any(a => a.Id == "https://www.w3.org/ns/activitystreams#Public") ? "unlisted"
                      : note.Data["to"].Any(a => a.Id == attributed.Data["followers"].First().Id) ? "private"
                      : "direct",
        media_attachments = new List<Attachment>(),
        mentions = new List<Mention>(),
        tags = new List<Tag>(),
        application = new Application { Name = "Kroeg", Website = "https://timon.com/kroeg" },
        language = null,
        pinned = false
      };

      foreach (var tag in note.Data["tag"])
      {
        var obj = tag.SubObject ?? (await _entityStore.GetEntity(tag.Id, true))?.Data;
        if (obj != null && obj.Type.Contains("https://www.w3.org/ns/activitystreams#Mention"))
        {
          var user = await _entityStore.GetEntity(obj["href"].First().Id, true);
          if (user == null) continue;
          status.mentions.Add(new Mention { id = Uri.EscapeDataString(user.Id), url = user.Id, username = (string)user.Data["preferredUsername"].FirstOrDefault().Primitive ?? user.Id, acct = user.Id });
        }
        else if (obj != null && obj.Type.Contains("http://joinmastodon.org/ns#Emoji"))
        {
          var emoji = obj["icon"].First().SubObject ?? (await _entityStore.GetEntity(obj["icon"].First().Id, false))?.Data;
          if (emoji == null) continue;
          status.emojis.Add(new Emoji { shortcode = ((string)obj["name"].FirstOrDefault()?.Primitive)?.Trim(':'), url = emoji["url"].FirstOrDefault()?.Id, static_url = emoji["url"].FirstOrDefault()?.Id });
        }
        else if (obj != null && obj.Type.Contains("https://www.w3.org/ns/activitystreams#Hashtag"))
        {
          status.tags.Add(new Tag { name = ((string)obj["name"].FirstOrDefault()?.Primitive)?.TrimStart('#'), url = obj["href"].FirstOrDefault()?.Id });
        }
      }
      int i = 0;

      foreach (var tag in note.Data["attachment"])
      {
        var obj = tag.SubObject ?? (await _entityStore.GetEntity(tag.Id, true))?.Data;
        if (obj == null) continue;

        if (obj["mediaType"].Any() && obj["url"].Any())
        {
          var mediaType = (string)obj["mediaType"].First().Primitive ?? "unknown";
          var url = (string)obj["url"].First().Id;
          var attachment = new Attachment
          {
            id = obj.Id ?? (note.Id + "#attachment/" + i.ToString()),
            type = mediaType.Split('/')[0],
            url = url,
            remote_url = url,
            preview_url = url,
            text_url = url,
            meta = new Dictionary<string, AttachmentMeta>
            {
              ["small"] = new AttachmentMeta { width = -1, height = -1, size = -1, aspect = -1 },
              ["original"] = new AttachmentMeta { width = -1, height = -1, size = -1, aspect = -1 }
            },
            description = (string)obj["name"].FirstOrDefault()?.Primitive
          };
          status.media_attachments.Add(attachment);
        }
      }

      if (note.Data["inReplyTo"].Any())
      {
        var reply = await _entityStore.GetEntity(note.Data["inReplyTo"].First().Id, true);
        if (reply != null)
          status.in_reply_to_account_id = Uri.EscapeDataString(reply.Data["attributedTo"].First().Id);
      }

      return status;
    }

    private async Task<Status> _translateStatus(CollectionTools.EntityCollectionItem item)
    {
      var isCreate = item.Entity.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Create");
      var isAnnounce = item.Entity.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Announce");
      if (!isCreate && !isAnnounce) return await _translateNote(item.Entity, null);

      var inner = await _translateNote(await _entityStore.GetEntity(item.Entity.Data["object"].First().Id, true), (isCreate && item.CollectionItemId >= 0) ? item.CollectionItemId.ToString() : null);
      if (inner == null) return null;

      if (isCreate) return inner;

      return new Status
      {
        id = item.CollectionItemId.ToString(),
        uri = inner.uri,
        url = inner.url,
        account = await _processAccount(await _entityStore.GetEntity(item.Entity.Data["actor"].First().Id, true)),
        in_reply_to_id = inner.in_reply_to_id,
        in_reply_to_account_id = inner.in_reply_to_account_id,
        reblog = inner,
        content = inner.content,
        created_at = DateTime.Parse((string)item.Entity.Data["published"].FirstOrDefault()?.Primitive ?? DateTime.Now.ToString()),
        emojis = inner.emojis,
        reblogs_count = inner.reblogs_count,
        favourites_count = inner.favourites_count,
        reblogged = inner.reblogged,
        favourited = inner.favourited,
        muted = inner.muted,
        sensitive = inner.sensitive,
        spoiler_text = inner.spoiler_text,
        visibility = inner.visibility,
        media_attachments = inner.media_attachments,
        mentions = inner.mentions,
        tags = inner.tags,
        application = inner.application,
        language = inner.language,
        pinned = inner.pinned
      };
    }

    private async Task<Notification> _translateNotification(CollectionTools.EntityCollectionItem item)
    {
      var userId = User.FindFirst(JwtTokenSettings.ActorClaim).Value;
      if (item.Entity.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Follow"))
      {
        return new Notification
        {
          id = item.CollectionItemId.ToString(),
          type = "follow",
          created_at = DateTime.Parse((string)item.Entity.Data["published"].FirstOrDefault()?.Primitive ?? item.Entity.Updated.ToString()),
          account = await _processAccount(await _entityStore.GetEntity(item.Entity.Data["actor"].First().Id, true)),
          status = null
        };
      }
      else if (item.Entity.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Announce") || item.Entity.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Like"))
      {
        var note = await _entityStore.GetEntity(item.Entity.Data["object"].First().Id, true);
        if (note == null || !note.Data["attributedTo"].Any(a => a.Id == userId)) return null;

        var status = await _translateNote(note, null);
        if (status == null) return null;

        return new Notification
        {
          id = item.CollectionItemId.ToString(),
          type = item.Entity.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Announce") ? "reblog" : "favourite",
          created_at = DateTime.Parse((string)item.Entity.Data["published"].FirstOrDefault()?.Primitive ?? item.Entity.Updated.ToString()),
          account = await _processAccount(await _entityStore.GetEntity(item.Entity.Data["actor"].First().Id, true)),
          status = status
        };
      }
      else if (item.Entity.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Create"))
      {
        var note = await _entityStore.GetEntity(item.Entity.Data["object"].First().Id, true);
        if (note == null) return null;

        var status = await _translateNote(note, null);
        if (status == null) return null;

        return new Notification
        {
          id = item.CollectionItemId.ToString(),
          type = "mention",
          created_at = DateTime.Parse((string)item.Entity.Data["published"].FirstOrDefault()?.Primitive ?? DateTime.Now.ToString()),
          account = await _processAccount(await _entityStore.GetEntity(item.Entity.Data["actor"].First().Id, true)),
          status = status
        };
      }

      return null;
    }

    [HttpPost("apps")]
    public IActionResult RegisterApplication(Application.Request request)
    {
      return Json(new Application.Response
      {
        Id = "1",
        ClientId = "id",
        ClientSecret = "secret"
      });
    }

    [HttpGet("accounts/verify_credentials")]
    public async Task<IActionResult> VerifyCredentials()
    {
      var userId = User.FindFirst(JwtTokenSettings.ActorClaim)?.Value;
      if (userId == null) return Unauthorized();

      var user = await _entityStore.GetEntity(userId, false);
      var parsed = await _processAccount(user);
      parsed.source = new Account.Source
      {
        privacy = "public",
        sensitive = false,
        note = parsed.note
      };

      return Json(parsed);
    }

    [HttpPatch("accounts/update_credentials")]
    public async Task<IActionResult> UpdateCredentials()
    {
      var userId = User.FindFirst(JwtTokenSettings.ActorClaim)?.Value;
      if (userId == null) return Unauthorized();

      var user = await _entityStore.GetEntity(userId, false);
      var parsed = await _processAccount(user);
      parsed.source = new Account.Source
      {
        privacy = "public",
        sensitive = false,
        note = parsed.note
      };

      return Json(parsed);
    }

    [HttpGet("accounts/{id}")]
    public async Task<IActionResult> GetAccount(string id)
    {
      var userId = User.FindFirst(JwtTokenSettings.ActorClaim)?.Value;
      if (userId == null) return Unauthorized();

      id = Uri.UnescapeDataString(id);
      var user = await _entityStore.GetEntity(id, true);
      if (user == null) return NotFound();

      return Json(await _processAccount(user));
    }

    [HttpGet("accounts/{id}/statuses")]
    public async Task<IActionResult> GetAccount(string id, string max_id, string since_id, int limit)
    {
      var userId = User.FindFirst(JwtTokenSettings.ActorClaim)?.Value;
      if (userId == null) return Unauthorized();

      id = Uri.UnescapeDataString(id);
      var user = await _entityStore.GetEntity(id, true);
      if (user == null) return NotFound();

      var me = await _entityStore.GetEntity(userId, false);

      return await _timeline(me.Data["inbox"].First().Id, max_id, since_id, limit, _translateStatus,
          new RelevantEntitiesService.AllStatement
          {
                    new RelevantEntitiesService.ContainsAnyStatement("rdf:type") { "https://www.w3.org/ns/activitystreams#Create", "https://www.w3.org/ns/activitystreams#Announce" },
                    new RelevantEntitiesService.ContainsAnyStatement("https://www.w3.org/ns/activitystreams#actor") { user.Id }
          }
      );
    }

    [HttpGet("statuses/{id}")]
    public async Task<IActionResult> GetStatus(string id)
    {
      var userId = User.FindFirst(JwtTokenSettings.ActorClaim)?.Value;
      if (userId == null) return Unauthorized();

      CollectionTools.EntityCollectionItem item = null;
      if (int.TryParse(id, out var idInt))
      {
        item = await _collectionTools.GetCollectionItem(idInt);
      }
      else
      {
        var ent = await _entityStore.GetEntity(Uri.UnescapeDataString(id), true);
        if (ent != null) item = new CollectionTools.EntityCollectionItem { CollectionItemId = -1, Entity = ent };
      }

      if (item == null) return NotFound();
      var translated = await _translateStatus(item);
      if (translated == null) return NotFound();
      return Json(translated);
    }

    [HttpGet("statuses/{id}/context")]
    public async Task<IActionResult> GetStatusContext(string id)
    {
      var userId = User.FindFirst(JwtTokenSettings.ActorClaim)?.Value;
      if (userId == null) return Unauthorized();

      CollectionTools.EntityCollectionItem item = null;
      if (int.TryParse(id, out var idInt))
      {
        item = await _collectionTools.GetCollectionItem(idInt);
      }
      else
      {
        var ent = await _entityStore.GetEntity(Uri.UnescapeDataString(id), true);
        if (ent != null) item = new CollectionTools.EntityCollectionItem { CollectionItemId = -1, Entity = ent };
      }

      if (item.Entity.Data["object"].Any())
        item.Entity = await _entityStore.GetEntity(item.Entity.Data["object"].First().Id, true);

      if (item == null) return NotFound();
      var res = new Context { ancestors = new List<Status>(), descendants = new List<Status>() };
      while (item.Entity.Data["inReplyTo"].Any())
      {
        var replyPost = await _entityStore.GetEntity(item.Entity.Data["inReplyTo"].First().Id, false);
        if (replyPost == null)
          break;

        item.Entity = replyPost;
        var translated = await _translateStatus(item);
        if (translated != null)
          res.ancestors.Add(translated);
      }

      res.ancestors.Reverse();

      return Json(res);
    }

    [HttpGet("statuses/{id}/card")]
    public IActionResult GetStatusCard(string id)
    {
      return Json(new { });
    }

    private delegate Task<T> _processItem<T>(CollectionTools.EntityCollectionItem item);

    private async Task<IActionResult> _timeline<T>(string id, string max_id, string since_id, int limit, _processItem<T> process, RelevantEntitiesService.IQueryStatement query = null)
    {
      if (!int.TryParse(max_id, out var fromId)) fromId = int.MaxValue;
      if (!int.TryParse(since_id, out var toId)) toId = int.MinValue;

      limit = Math.Min(40, Math.Max(20, limit));
      var parsed = new List<T>();
      string links = null;
      while (parsed.Count < limit)
      {
        var items = await _collectionTools.GetItems(id, fromId, toId, limit + 1,
            query ?? new RelevantEntitiesService.ContainsAnyStatement("rdf:type") {
                        "https://www.w3.org/ns/activitystreams#Create", "https://www.w3.org/ns/activitystreams#Announce"
            });
        if (items.Count == 0) break;

        if (links == null)
          links = $"<{Request.Scheme}://{Request.Host.ToUriComponent()}{Request.Path}?since_id={items[0].CollectionItemId}>; rel=\"prev\"";

        foreach (var item in items)
        {
          if (parsed.Count >= limit)
          {
            links += $", <{Request.Scheme}://{Request.Host.ToUriComponent()}{Request.Path}?max_id={item.CollectionItemId}>; rel=\"next\"";
            break;
          }
          try
          {
            var translated = await process(item);
            if (translated != null) parsed.Add(translated);
          }
          catch (Exception e)
          {
            Console.WriteLine(e);
          }

          toId = int.MinValue;
          fromId = item.CollectionItemId;
        }

      }

      if (links != null)
        Response.Headers.Add("Link", links);

      return Json(parsed);
    }

    private async Task<IActionResult> _queryPublic<T>(string max_id, string since_id, int limit, _processItem<T> process)
    {
      if (!int.TryParse(max_id, out var fromId)) fromId = int.MaxValue;
      if (!int.TryParse(since_id, out var toId)) toId = int.MinValue;

      limit = Math.Min(40, Math.Max(20, limit));
      var parsed = new List<T>();
      string links = null;
      while (parsed.Count < limit)
      {
        var items = await _relevantEntities.Query(
            new RelevantEntitiesService.AllStatement
            {
                        new RelevantEntitiesService.ContainsAnyStatement("rdf:type")
                        {
                            "https://www.w3.org/ns/activitystreams#Create", "https://www.w3.org/ns/activitystreams#Announce"
                        },
                        new RelevantEntitiesService.AnyStatement
                        {
                            new RelevantEntitiesService.ContainsAnyStatement("https://www.w3.org/ns/activitystreams#to") { "https://www.w3.org/ns/activitystreams#Public" },
                            new RelevantEntitiesService.ContainsAnyStatement("https://www.w3.org/ns/activitystreams#bto") { "https://www.w3.org/ns/activitystreams#Public" },
                        }
            }, fromId, toId, limit);
        if (items.Count == 0) break;

        if (links == null)
          links = $"<{Request.Scheme}://{Request.Host.ToUriComponent()}{Request.Path}?since_id={items[0].DbId}>; rel=\"prev\"";

        foreach (var item in items)
        {
          if (parsed.Count >= limit)
          {
            links += $", <{Request.Scheme}://{Request.Host.ToUriComponent()}{Request.Path}?max_id={item.DbId}>; rel=\"next\"";
            break;
          }
          try
          {
            var translated = await process(new CollectionTools.EntityCollectionItem { CollectionItemId = -1, Entity = item });
            if (translated != null) parsed.Add(translated);
          }
          catch (Exception e)
          {
            Console.WriteLine(e);
          }

          toId = int.MinValue;
          fromId = item.DbId;
        }

      }

      if (links != null)
        Response.Headers.Add("Link", links);

      return Json(parsed);
    }

    [HttpGet("timelines/home")]
    public async Task<IActionResult> HomeTimeline(string max_id, string since_id, int limit)
    {
      var userId = User.FindFirst(JwtTokenSettings.ActorClaim)?.Value;
      if (userId == null) return Unauthorized();

      var user = await _entityStore.GetEntity(userId, false);
      return await _timeline(user.Data["inbox"].First().Id, max_id, since_id, limit, _translateStatus);
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> NotificationTimeline(string max_id, string since_id, int limit)
    {
      var userId = User.FindFirst(JwtTokenSettings.ActorClaim)?.Value;
      if (userId == null) return Unauthorized();

      var user = await _entityStore.GetEntity(userId, false);
      return await _timeline(user.Data["inbox"].First().Id, max_id, since_id, limit, _translateNotification,
          new RelevantEntitiesService.AnyStatement
          {
                    new RelevantEntitiesService.AllStatement
                    {
                        new RelevantEntitiesService.ContainsAnyStatement("rdf:type") { "https://www.w3.org/ns/activitystreams#Follow" },
                        new RelevantEntitiesService.ContainsAnyStatement("https://www.w3.org/ns/activitystreams#object") { userId }
                    },
                    new RelevantEntitiesService.AllStatement
                    {
                        new RelevantEntitiesService.ContainsAnyStatement("rdf:type")
                        {
                            "https://www.w3.org/ns/activitystreams#Announce",
                            "https://www.w3.org/ns/activitystreams#Create"
                        },
                        new RelevantEntitiesService.AnyStatement
                        {
                            new RelevantEntitiesService.ContainsAnyStatement("https://www.w3.org/ns/activitystreams#to") { userId },
                            new RelevantEntitiesService.ContainsAnyStatement("https://www.w3.org/ns/activitystreams#cc") { userId },
                            new RelevantEntitiesService.ContainsAnyStatement("https://www.w3.org/ns/activitystreams#bto") { userId },
                            new RelevantEntitiesService.ContainsAnyStatement("https://www.w3.org/ns/activitystreams#bcc") { userId }
                        }
                    },
                    new RelevantEntitiesService.ContainsAnyStatement("rdf:type")
                    {
                        "https://www.w3.org/ns/activitystreams#Like"
                    }
          }
      );
    }

    [HttpGet("timelines/public")]
    public async Task<IActionResult> PublicTimeline(string max_id, string since_id, int limit)
    {
      var userId = User.FindFirst(JwtTokenSettings.ActorClaim)?.Value;
      if (userId == null) return Unauthorized();

      return await _queryPublic(max_id, since_id, limit, _translateStatus);
    }
  }
}
