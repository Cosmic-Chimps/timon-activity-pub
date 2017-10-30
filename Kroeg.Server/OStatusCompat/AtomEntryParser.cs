using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Kroeg.Server.Services;

namespace Kroeg.Server.OStatusCompat
{
    public class AtomEntryParser
    {
        private static string _objectTypeToType(string objectType)
        {
            if (objectType == null) return null;

            // length: 35
            if (objectType.StartsWith("http://activitystrea.ms/schema/1.0/"))
                objectType = objectType.Substring(35);
            else if (objectType.StartsWith("http://ostatus.org/schema/1.0/"))
                objectType = objectType.Substring(30);
            if (!objectType.StartsWith("http"))
                objectType = objectType.Substring(0, 1).ToUpperInvariant() + objectType.Substring(1);

            if (objectType == "Comment") objectType = "Note";

            return "https://www.w3.org/ns/activitystreams#" + objectType;
        }

        private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
        private static readonly XNamespace AtomMedia = "http://purl.org/syndication/atommedia";
        private static readonly XNamespace AtomThreading = "http://purl.org/syndication/thread/1.0";
        private static readonly XNamespace ActivityStreams = "http://activitystrea.ms/spec/1.0/";
        private static readonly XNamespace PortableContacts = "http://portablecontacts.net/spec/1.0";
        private static readonly XNamespace OStatus = "http://ostatus.org/schema/1.0";
        private static readonly XNamespace NoNamespace = "";

        private readonly IEntityStore _entityStore;
        private readonly EntityData _entityConfiguration;
        private readonly APContext _context;
        private readonly RelevantEntitiesService _relevantEntities;

        private ASObject _parseAuthor(XElement element)
        {
            var ao = new ASObject();
            ao.Type.Add("https://www.w3.org/ns/activitystreams#Person");

            // set preferredUsername and name
            {
                var atomName = element.Element(Atom + "name")?.Value;
                var pocoDisplayName = element.Element(PortableContacts + "displayName")?.Value;
                var pocoPreferredUsername = element.Element(PortableContacts + "preferredUsername")?.Value;

                ao.Replace("preferredUsername", ASTerm.MakePrimitive(pocoPreferredUsername ?? atomName));
                ao.Replace("name", ASTerm.MakePrimitive(pocoDisplayName ?? pocoPreferredUsername ?? atomName));
            }

            // set summary
            {
                var atomSummary = element.Element(Atom + "summary")?.Value;
                var pocoNote = element.Element(PortableContacts + "note")?.Value;

                ao.Replace("summary", ASTerm.MakePrimitive(pocoNote ?? atomSummary));
            }

            string retrievalUrl = null;

            {
                foreach (var link in element.Elements(Atom + "link"))
                {
                    var rel = link.Attribute(NoNamespace + "rel")?.Value;
                    var type = link.Attribute(NoNamespace + "type")?.Value;
                    var href = link.Attribute(NoNamespace + "href")?.Value;

                    switch (rel)
                    {
                        case "avatar":
                            var avatarObject = new ASObject();
                            avatarObject.Type.Add("https://www.w3.org/ns/activitystreams#Image");
                            avatarObject.Replace("mediaType", ASTerm.MakePrimitive(type));
                            var width = link.Attribute(AtomMedia + "width")?.Value;
                            var height = link.Attribute(AtomMedia + "height")?.Value;

                            if (width != null && height != null)
                            {
                                avatarObject.Replace("width", ASTerm.MakePrimitive(int.Parse(width)));
                                avatarObject.Replace("height", ASTerm.MakePrimitive(int.Parse(height)));
                            }

                            avatarObject.Replace("url", ASTerm.MakePrimitive(href));

                            ao["icon"].Add(ASTerm.MakeSubObject(avatarObject));
                            break;
                        case "alternate":
                            if (type == "text/html")
                            {
                                if (retrievalUrl == null)
                                    retrievalUrl = href;

                                ao["atomUri"].Add(ASTerm.MakePrimitive(href));
                            }
                            break;
                        case "self":
                            if (type == "application/atom+xml")
                                retrievalUrl = href;
                            break;
                    }
                }
            }

            // should be Mastodon *and* GNU social compatible: Mastodon uses uri as id

            if (element.Element(Atom + "id") != null)
                ao.Id = element.Element(Atom + "id")?.Value;
            else
                ao.Id = element.Element(Atom + "uri")?.Value;

            if (element.Element(Atom + "uri") != null)
                ao["url"].Add(ASTerm.MakePrimitive(element.Element(Atom + "uri")?.Value));

            if (element.Element(Atom + "email") != null)
                ao["email"].Add(ASTerm.MakePrimitive(element.Element(Atom + "email")?.Value));

            foreach (var url in element.Elements(PortableContacts + "urls"))
                ao["url"].Add(ASTerm.MakePrimitive(url.Element(PortableContacts + "value")?.Value));

            if (retrievalUrl != null)
                ao.Replace("atomUri", ASTerm.MakePrimitive(retrievalUrl));
            
            return ao;
        }

        private async Task<string> _findInReplyTo(string atomId)
        {
            var entity = await _entityStore.GetEntity(atomId, true);
            if (entity == null) return atomId;
            if (entity.Type.Contains("https://www.w3.org/ns/activitystreams#Create"))
            {
                return entity.Data["object"].Single().Id;
            }
            return atomId;
        }

        [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
        private async Task<ASTerm> _parseActivityObject(XElement element, string authorId, string targetUser, bool isActivity = false)
        {
            if (!isActivity && element.Element(ActivityStreams + "verb") != null) return await _parseActivity(element, authorId, targetUser);
            var entity = await _entityStore.GetEntity(element.Element(Atom + "id")?.Value, true);
            if (entity != null)
            {
                if (entity.Type.Contains("https://www.w3.org/ns/activitystreams#Create"))
                    return ASTerm.MakeId(entity.Data["object"].First().Id);

                return ASTerm.MakeId(element.Element(Atom + "id")?.Value);
            }

            var ao = new ASObject();
            ao.Id = element.Element(Atom + "id")?.Value + (isActivity ? "#object" : "");
            ao.Replace("kroeg:origin", ASTerm.MakePrimitive("atom"));

            var objectType = _objectTypeToType(element.Element(ActivityStreams + "object-type")?.Value);
            if (objectType == "https://www.w3.org/ns/activitystreams#Person")
                return ASTerm.MakeSubObject(_parseAuthor(element));

            ao.Type.Add(objectType);
            ao.Replace("attributedTo", ASTerm.MakeId(authorId));


            if (element.Element(Atom + "summary") != null)
                ao.Replace("summary", ASTerm.MakePrimitive(element.Element(Atom + "summary")?.Value));
            if (element.Element(Atom + "published") != null)
                ao.Replace("published", ASTerm.MakePrimitive(element.Element(Atom + "published")?.Value));
            if (element.Element(Atom + "updated") != null)
                ao.Replace("updated", ASTerm.MakePrimitive(element.Element(Atom + "updated")?.Value));

            ao.Replace("content", ASTerm.MakePrimitive(element.Element(Atom + "content")?.Value));
            var mediaType = element.Element(Atom + "content")?.Attribute(NoNamespace + "type")?.Value;

            if (mediaType != null)
            {
                if (mediaType == "text") mediaType = "text/plain";
                if (mediaType.Contains("/")) ao.Replace("mediaType", ASTerm.MakePrimitive(mediaType));
            }

            if (element.Element(OStatus + "conversation") != null)
                ao.Replace("conversation", ASTerm.MakePrimitive(element.Element(OStatus + "conversation").Attribute(NoNamespace + "ref")?.Value ?? element.Element(OStatus + "conversation").Value));

            if (element.Element(AtomThreading + "in-reply-to") != null)
            {
                var elm = element.Element(AtomThreading + "in-reply-to");
                var @ref = await _findInReplyTo(elm.Attribute(NoNamespace + "ref").Value);
                var hrel = elm.Attribute(NoNamespace + "href")?.Value;

                if (hrel == null)
                    ao.Replace("inReplyTo", ASTerm.MakeId(@ref));
                else if (await _entityStore.GetEntity(@ref, false) != null)
                {
                    ao.Replace("inReplyTo", ASTerm.MakeId(@ref));
                }
                else
                {
                    var lazyLoad = new ASObject();
                    lazyLoad.Id = @ref;
                    lazyLoad.Type.Add("_:LazyLoad");
                    lazyLoad.Replace("href", ASTerm.MakePrimitive(hrel));
                    ao.Replace("inReplyTo", ASTerm.MakeSubObject(lazyLoad));
                }
            }

            foreach (var tag in element.Elements(Atom + "category"))
            {
                var val = tag.Attribute(NoNamespace + "term").Value;

                var tagao = new ASObject();
                tagao.Id = $"{_entityConfiguration.BaseUri}/tag/{val}";
                tagao["name"].Add(ASTerm.MakePrimitive("#" + val));
                tagao.Type.Add("Hashtag");

                ao["tag"].Add(ASTerm.MakeSubObject(tagao));
            }

            string retrievalUrl = null;

            foreach (var link in element.Elements(Atom + "link"))
            {
                var rel = link.Attribute(NoNamespace + "rel").Value;
                var type = link.Attribute(NoNamespace + "type")?.Value;
                var href = link.Attribute(NoNamespace + "href").Value;
                
                if (rel == "self" && type == "application/atom+xml")
                    retrievalUrl = href;
                else if (rel == "alternate" && type == "text/html")
                {
                    ao["url"].Add(ASTerm.MakePrimitive(href));

                    if (retrievalUrl == null) retrievalUrl = href;
                }
                else if (rel == "mentioned")
                {
                    if (href == "http://activityschema.org/collection/public")
                        href = "https://www.w3.org/ns/activitystreams#Public";

                    ao["to"].Add(ASTerm.MakeId(href));
                }
                else if (rel == "enclosure")
                {
                    // image
                    var subAo = new ASObject();
                    subAo["url"].Add(ASTerm.MakePrimitive(href));
                    subAo["mediaType"].Add(ASTerm.MakePrimitive(type));

                    switch (type.Split('/')[0])
                    {
                        case "image":
                            subAo.Type.Add("https://www.w3.org/ns/activitystreams#Image");
                            break;
                        case "video":
                            subAo.Type.Add("https://www.w3.org/ns/activitystreams#Video");
                            break;
                        default:
                            continue;
                    }

                    if (link.Attribute(NoNamespace + "length") != null)
                        subAo["fileSize"].Add(ASTerm.MakePrimitive(int.Parse(link.Attribute(NoNamespace + "length").Value)));

                    ao["attachment"].Add(ASTerm.MakeSubObject(subAo));
                }
            }
            
            return ASTerm.MakeSubObject(ao);
        }

        [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
        private async Task<ASTerm> _parseActivity(XElement element, string authorId, string targetUser)
        {
            if (await _isSelf(element.Element(Atom + "id").Value)) return ASTerm.MakeId(await _fixActivityToObjectId(element.Element(Atom + "id").Value));

            var ao = new ASObject();
            ao.Id = element.Element(Atom + "id").Value;
            ao.Replace("_:origin", ASTerm.MakePrimitive("atom"));

            var verb = _objectTypeToType(element.Element(ActivityStreams + "verb")?.Value) ?? "Post";
            var originalVerb = verb;

            if (verb == "Unfollow" && (await _entityStore.GetEntity(element.Element(Atom + "id").Value, false))?.Type == "Follow") // egh egh egh, why, mastodon
                ao.Id += "#unfollow";

            if (verb == "Unfavorite") verb = "Undo";
            if (verb == "Unfollow") verb = "Undo";
            if (verb == "Request-friend") return null;

            if (verb == "Post") verb = "Create";
            else if (verb == "Share") verb = "Announce";
            else if (verb == "Favorite") verb = "Like";


#pragma warning disable 618
            if (!_entityConfiguration.IsActivity(verb)) return null;
#pragma warning restore 618

            ao.Type.Add("https://www.w3.org/ns/activitystreams#" + verb);

            if (element.Element(Atom + "title") != null)
                ao.Replace("summary", ASTerm.MakePrimitive(element.Element(Atom + "title").Value));
            if (element.Element(Atom + "published") != null)
                ao.Replace("published", ASTerm.MakePrimitive(element.Element(Atom + "published").Value));
            if (element.Element(Atom + "updated") != null)
                ao.Replace("updated", ASTerm.MakePrimitive(element.Element(Atom + "updated").Value));

            if (element.Element(Atom + "author") != null)
            {
                var newAuthor = _parseAuthor(element.Element(Atom + "author"));
                authorId = newAuthor.Id;
            }

            if (authorId != null)
                ao.Replace("actor", ASTerm.MakeId(authorId));


            string retrievalUrl = null;

            foreach (var link in element.Elements(Atom + "link"))
            {
                var rel = link.Attribute(NoNamespace + "rel").Value;
                var type = link.Attribute(NoNamespace + "type")?.Value;
                var href = link.Attribute(NoNamespace + "href").Value;

                if (rel == "self" && type == "application/atom+xml")
                    retrievalUrl = href;
                else if (rel == "alternate" && type == "text/html")
                {
                    ao["url"].Add(ASTerm.MakePrimitive(href));

                    if (retrievalUrl == null) retrievalUrl = href;
                }
                else if (rel == "mentioned")
                {
                    if (href == "http://activityschema.org/collection/public")
                        href = "https://www.w3.org/ns/activitystreams#Public";

                    ao["to"].Add(ASTerm.MakeId(href));
                }
            }

            if (targetUser != null)
                ao["cc"].Add(ASTerm.MakeId(targetUser));

            if (retrievalUrl != null)
                ao.Replace("atomUri", ASTerm.MakePrimitive(retrievalUrl));

            if (element.Element(ActivityStreams + "object") != null)
            {
                var parsedActivityObject = await _parseActivityObject(element.Element(ActivityStreams + "object"), authorId, targetUser);

                if (verb == "Undo" && originalVerb == "Unfavorite")
                {
                    parsedActivityObject = ASTerm.MakeId((await _relevantEntities.FindRelevantObject(authorId, "https://www.w3.org/ns/activitystreams#Like", _getId(parsedActivityObject))).FirstOrDefault()?.Id);
                }
                else if (verb == "Undo" && originalVerb == "Unfollow")
                    parsedActivityObject = ASTerm.MakeId((await _relevantEntities.FindRelevantObject(authorId, "https://www.w3.org/ns/activitystreams#Follow", _getId(parsedActivityObject))).FirstOrDefault()?.Id);

                ao.Replace("object", parsedActivityObject);
            }
            else if (element.Element(ActivityStreams + "object-type") == null && originalVerb == "Unfollow")
            {
                // you thought Mastodon was bad?
                // GNU Social doesn't send an object in an unfollow.

                // .. what

                ao.Replace("object", ASTerm.MakeId((await _relevantEntities.FindRelevantObject(authorId, "https://www.w3.org/ns/activitystreams#Follow", targetUser)).FirstOrDefault()?.Id));
            }
            else
            {
                ao.Replace("object", await _parseActivityObject(element, authorId, targetUser, true));
            }

            return ASTerm.MakeSubObject(ao);
        }

        private string _getId(ASTerm term)
        {
            if (term.Primitive != null) return (string) term.Primitive;
            return term.SubObject.Id;
        }

        private async Task<bool> _isSelf(string id)
        {
            var getId = await _entityStore.GetEntity(id, false);
            return getId?.IsOwner == true;
        }

        private async Task<string> _fixActivityToObjectId(string id)
        {
            if (!await _isSelf(id)) return id;
            return (string) (await _entityStore.GetEntity(id, false)).Data["object"].First().Primitive;
        }

        [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
        private async Task<ASObject> _parseFeed(XElement element, string targetUser)
        {
            var ao = new ASObject();
            ao.Type.Add("https://www.w3.org/ns/activitystreams#OrderedCollectionPage");
            ao.Replace("_:origin", ASTerm.MakePrimitive("atom"));
            ao.Id = element.Element(Atom + "id").Value;

            if (element.Element(Atom + "title") != null)
                ao.Replace("summary", ASTerm.MakePrimitive(element.Element(Atom + "title").Value));
            if (element.Element(Atom + "updated") != null)
                ao.Replace("updated", ASTerm.MakePrimitive(element.Element(Atom + "updated").Value));

            var author = _parseAuthor(element.Element(Atom + "author"));
            ao.Replace("attributedTo", ASTerm.MakeSubObject(author));

            var authorId = author.Id;

            foreach (var entry in element.Elements(Atom + "entry"))
                ao["orderedItems"].Add(await _parseActivity(entry, authorId, targetUser));


            foreach (var link in element.Elements(Atom + "link"))
            {
                var rel = link.Attribute(NoNamespace + "rel").Value;
                var type = link.Attribute(NoNamespace + "type")?.Value;
                var href = link.Attribute(NoNamespace + "href").Value;

                if (rel == "alternate" && type == "text/html")
                {
                    ao["url"].Add(ASTerm.MakePrimitive(href));
                }
                else if (rel == "self" && type == "application/atom+xml")
                {
                    author.Replace("atomUri", ASTerm.MakePrimitive(href));
                }
                else switch (rel)
                {
                    case "salmon":
                        ao["_:salmonUrl"].Add(ASTerm.MakePrimitive(href));
                        break;
                    case "hub":
                        ao["_:hubUrl"].Add(ASTerm.MakePrimitive(href));
                        break;
                    case "prev":
                        ao["prev"].Add(ASTerm.MakeId(href));
                        break;
                    case "next":
                        ao["next"].Add(ASTerm.MakeId(href));
                        break;
                }
            }

            author["_:salmonUrl"].AddRange(ao["_:salmonUrl"]);
            author["_:hubUrl"].AddRange(ao["_:hubUrl"]);

            return ao;
        }

        public AtomEntryParser(IEntityStore entityStore, EntityData entityConfiguration, APContext context, RelevantEntitiesService relevantEntities)
        {
            _entityStore = entityStore;
            _entityConfiguration = entityConfiguration;
            _context = context;
            _relevantEntities = relevantEntities;
        }

        public async Task<ASObject> Parse(XDocument doc, bool translateSingleActivity, string targetUser)
        {
            if (doc.Root?.Name == Atom + "entry")
                return (await _parseActivity(doc.Root, null, targetUser)).SubObject;
            var feed = await _parseFeed(doc.Root, targetUser);
            if (feed["orderedItems"].Count == 1 && translateSingleActivity)
                return feed["orderedItems"].First().SubObject;
            return feed;
        }
    }
}
