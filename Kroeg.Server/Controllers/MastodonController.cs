using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.Server.Configuration;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Microsoft.AspNetCore.Mvc;

namespace Kroeg.Server.Controllers
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

        private async Task<Mastodon.Account> _processAccount(APEntity entity)
        {
            var result = new Mastodon.Account
            {
                id = Uri.EscapeDataString(entity.Id),
                username = (string) entity.Data["preferredUsername"].FirstOrDefault()?.Primitive ?? entity.Id,
                display_name = (string) entity.Data["name"].FirstOrDefault()?.Primitive ?? entity.Id,
                locked = entity.Data["manuallyApprovesFollowers"].Any(a => (bool) a.Primitive),
                created_at = DateTime.Now,
                note = (string) entity.Data["summary"].FirstOrDefault()?.Primitive ?? "",
                url = (string) entity.Data["url"].FirstOrDefault()?.Primitive ?? entity.Id,
                moved = null,

                followers_count = -1,
                following_count = -1,
                statuses_count = -1
            };

            if (entity.IsOwner) result.acct = result.username;
            else
                result.acct = result.username + "@" + (new Uri(entity.Id)).Host;

            if (entity.Data["icon"].Any())
                result.avatar = result.avatar_static = entity.Data["icon"].First().Id ?? entity.Data["icon"].First().SubObject["url"].First().Id;

            var followers = await _entityStore.GetEntity(entity.Data["followers"].First().Id, false);
            if (followers != null && followers.Data["totalItems"].Any())
                result.followers_count = (int) followers.Data["totalItems"].First().Primitive;                

            var following = await _entityStore.GetEntity(entity.Data["following"].First().Id, false);
            if (following != null && following.Data["totalItems"].Any())
                result.following_count = (int) following.Data["totalItems"].First().Primitive;                

            var outbox = await _entityStore.GetEntity(entity.Data["outbox"].First().Id, false);
            if (outbox != null && outbox.Data["totalItems"].Any())
                result.statuses_count = (int) outbox.Data["totalItems"].First().Primitive;

            return result;         
        }

        private async Task<Mastodon.Status> _translateNote(APEntity note, string id)
        {
            if (note == null) return null;
            if (note.Type != "https://www.w3.org/ns/activitystreams#Note") return null;

            var attributed = await _entityStore.GetEntity(note.Data["attributedTo"].First().Id, true);

            var status = new Mastodon.Status
            {
                id = id ?? Uri.EscapeDataString(note.Id),
                uri = note.Id,
                url = note.Data["url"].FirstOrDefault()?.Id ?? note.Id,
                account = await _processAccount(attributed),
                in_reply_to_id = note.Data["inReplyTo"].Any() ? Uri.EscapeDataString(note.Data["inReplyTo"].FirstOrDefault()?.Id) : null,
                reblog = null,
                content = (string) note.Data["content"].First().Primitive,
                created_at = DateTime.Parse((string) note.Data["published"].First().Primitive ?? DateTime.Now.ToString()),
                emojis = new string[] {},
                reblogs_count = 0,
                favourites_count = 0,
                reblogged = false,
                favourited = false,
                muted = false,
                sensitive = note.Data["sensitive"].Any(a => (bool) a.Primitive),
                spoiler_text = (string) note.Data["summary"].FirstOrDefault()?.Primitive,
                visibility = note.Data["to"].Any(a => a.Id == "https://www.w3.org/ns/activitystreams#Public") ? "public"
                            : note.Data["cc"].Any(a => a.Id == "https://www.w3.org/ns/activitystreams#Public") ? "unlisted"
                            : note.Data["to"].Any(a => a.Id == attributed.Data["followers"].First().Id) ? "private"
                            : "direct",
                media_attachments = new string[] {},
                mentions = new string[] {},
                tags = new string[] {},
                application = new Mastodon.Application { Name = "Kroeg", Website = "https://puckipedia.com/kroeg" },
                language = null,
                pinned = false
            };

            if (note.Data["inReplyTo"].Any())
            {
                var reply = await _entityStore.GetEntity(note.Data["inReplyTo"].First().Id, true);
                if (reply != null)
                    status.in_reply_to_account_id = Uri.EscapeDataString(reply.Data["attributedTo"].First().Id);
            }

            return status;
        }

        private async Task<Mastodon.Status> _translateStatus(CollectionTools.EntityCollectionItem item)
        {
            var isCreate = item.Entity.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Create");
            var isAnnounce = item.Entity.Data.Type.Contains("https://www.w3.org/ns/activitystreams#Announce");
            if (!isCreate && !isAnnounce) return await _translateNote(item.Entity, null);

            var inner = await _translateNote(await _entityStore.GetEntity(item.Entity.Data["object"].First().Id, true), (isCreate && item.CollectionItemId >= 0) ? item.CollectionItemId.ToString() : null);
            if (inner == null) return null;

            if (isCreate) return inner;

            return new Mastodon.Status
            {
                id = item.CollectionItemId.ToString(),
                uri = inner.uri,
                url = inner.url,
                account = await _processAccount(await _entityStore.GetEntity(item.Entity.Data["actor"].First().Id, true)),
                in_reply_to_id = inner.in_reply_to_id,
                in_reply_to_account_id = inner.in_reply_to_account_id,
                reblog = inner,
                content = inner.content,
                created_at = DateTime.Parse((string) item.Entity.Data["published"].First().Primitive ?? DateTime.Now.ToString()),
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

        [HttpPost("apps")]
        public IActionResult RegisterApplication(Mastodon.Application.Request request)
        {
            return Json(new Mastodon.Application.Response
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
            parsed.source = new Mastodon.Account.Source {
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
            parsed.source = new Mastodon.Account.Source {
                privacy = "public",
                sensitive = false,
                note = parsed.note
            };

            return Json(parsed);
        }

        [HttpGet("accounts/{id}")]
        public async Task<IActionResult> GetAccount(string id)
        {
            id = Uri.UnescapeDataString(id);
            var user = await _entityStore.GetEntity(id, true);
            if (user == null) return NotFound();

            return Json(await _processAccount(user));
        }

        [HttpGet("statuses/{id}")]
        public async Task<IActionResult> GetStatus(string id)
        {
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
            var res = new Mastodon.Context { ancestors = new List<Mastodon.Status>(), descendants = new List<Mastodon.Status>() };
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
            return Json(new {});
        }

        private async Task<IActionResult> _timeline(string id, string max_id, string since_id, int limit)
        {
            if (!int.TryParse(max_id, out var fromId)) fromId = int.MaxValue;
            if (!int.TryParse(since_id, out var toId)) toId = int.MinValue;

            limit = Math.Min(40, Math.Max(20, limit));
            var parsed = new List<Mastodon.Status>();
            string links = null;
            while (parsed.Count < limit)
            {
                var items = await _collectionTools.GetItems(id, fromId, toId, limit + 1);
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
                    var translated = await _translateStatus(item);
                    if (translated != null) parsed.Add(translated);

                    toId = int.MinValue;
                    fromId = item.CollectionItemId;
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
            return await _timeline(user.Data["inbox"].First().Id, max_id, since_id, limit);
        }
    }
}