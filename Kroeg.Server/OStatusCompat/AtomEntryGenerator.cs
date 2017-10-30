﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;

namespace Kroeg.Server.OStatusCompat
{
    public class AtomEntryGenerator
    {
        private IEntityStore _entityStore;
        private readonly EntityData _entityConfiguration;
        private readonly ActivityService _activityService;

        private static string _typeToObjectType(string type, bool isReply = false)
        {
            if (type == null) return null;

            type = type.Replace("https://www.w3.org/ns/activitystreams#", "");

            if (isReply && type == "Note") type = "Comment";

            if (type == "Unfollow")
                type = "http://ostatus.org/schema/1.0/" + type.ToLower();
            else if (!type.StartsWith("http"))
                type = "http://activitystrea.ms/schema/1.0/" + type.ToLower();

            return type;
        }

        private static string _makeAtomUrl(string bnurl)
        {
            var uri = new UriBuilder(bnurl);
            uri.Path += ".atom";

            return uri.ToString();
        }

        private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
        private static readonly XNamespace AtomMedia = "http://purl.org/syndication/atommedia";
        private static readonly XNamespace AtomThreading = "http://purl.org/syndication/thread/1.0";
        private static readonly XNamespace ActivityStreams = "http://activitystrea.ms/spec/1.0/";
        private static readonly XNamespace PortableContacts = "http://portablecontacts.net/spec/1.0";
        private static readonly XNamespace OStatus = "http://ostatus.org/schema/1.0";
        private static readonly XNamespace NoNamespace = "";

        private XElement _buildAvatarLink(ASObject ao)
        {
            var elem = new XElement(Atom + "link",
                new XAttribute(NoNamespace + "rel", "avatar"));

            if (!ao.Type.Contains("https://www.w3.org/ns/activitystreams#Image")) return elem;

            if (ao["mediaType"].Any())
                elem.Add(new XAttribute(NoNamespace + "type", ao["mediaType"].First().Primitive));

            if (ao["width"].Any() && ao["height"].Any())
                elem.Add(
                    new XAttribute(AtomMedia + "width", ao["width"].First().Primitive),
                    new XAttribute(AtomMedia + "height", ao["height"].First().Primitive));

            elem.Add(new XAttribute(NoNamespace + "href", ao["url"].First().Primitive));

            return elem;
        }

        private XElement _createAuthor(ASObject ao)
        {
            var elem = new XElement(Atom + "author");

            elem.Add(new XElement(ActivityStreams + "object-type", _typeToObjectType("Person")));

            // set preferredUsername and name
            {
                var preferredUsername = ao["preferredUsername"].FirstOrDefault()?.Primitive;
                var name = ao["name"].FirstOrDefault()?.Primitive ?? preferredUsername;
                preferredUsername = preferredUsername ?? name;

                elem.Add(new XElement(PortableContacts + "displayName", name));
                elem.Add(new XElement(Atom + "name", name));
                elem.Add(new XElement(PortableContacts + "preferredUsername", preferredUsername));
                elem.Add(new XElement(Atom + "email", ao["email"].FirstOrDefault()?.Primitive ?? $"{preferredUsername}@{_entityConfiguration.BaseDomain}"));
            }

            {
                var summary = ao["summary"].FirstOrDefault()?.Primitive;
                if (summary != null)
                {
                    elem.Add(new XElement(Atom + "summary", summary));
                    elem.Add(new XElement(PortableContacts + "note", summary));
                }
            }

            foreach (var icon in ao["icon"])
            {
                if (icon.Primitive != null) // just a link
                    elem.Add(
                        new XElement(Atom + "link",
                        new XAttribute(NoNamespace + "rel", "avatar"),
                        new XAttribute(NoNamespace + "href", icon.Primitive)));
                else
                    elem.Add(_buildAvatarLink(icon.SubObject));
            }

            var id = ao.Id;

            var selfAlternate = ao["_:atomAlternate"].FirstOrDefault()?.Primitive ?? id;
            elem.Add(
                new XElement(Atom + "link",
                new XAttribute(NoNamespace + "rel", "alternate"),
                new XAttribute(NoNamespace + "type", "text/html"),
                new XAttribute(NoNamespace + "href", selfAlternate)));


            elem.Add(
                new XElement(Atom + "uri", id),
                new XElement(Atom + "id", id));

            return elem;
        }

        private static readonly Dictionary<string, string> VerbTranslation = new Dictionary<string, string>
        {
            ["https://www.w3.org/ns/activitystreams#Create"] = "Post",
            ["https://www.w3.org/ns/activitystreams#Announce"] = "Share",
            ["https://www.w3.org/ns/activitystreams#Like"] = "Favorite"
        };

        private XElement _buildMention(ASTerm term)
        {
            var objectType = "Person";
            var  s = (string) term.Id;

            if (s == "https://www.w3.org/ns/activitystreams#Public")
            {
                s = "http://activityschema.org/collection/public";
                objectType = "Collection";
            }

            return new XElement(Atom + "link",
                new XAttribute(NoNamespace + "rel", "mentioned"),
                new XAttribute(OStatus + "object-type", _typeToObjectType(objectType)),
                new XAttribute(NoNamespace + "href", s));
        }

        private static void _setNamespaces(XElement elem)
        {
            elem.Add(
                new XAttribute(XNamespace.Xmlns + "thr", AtomThreading),
                new XAttribute(XNamespace.Xmlns + "activity", ActivityStreams),
                new XAttribute(XNamespace.Xmlns + "poco", PortableContacts),
                new XAttribute(XNamespace.Xmlns + "media", AtomMedia),
                new XAttribute(XNamespace.Xmlns + "ostatus", OStatus)
                );
        }

        private XElement _buildAttachment(ASObject ao)
        {
            var elem = new XElement(Atom + "link",
                new XAttribute(NoNamespace + "rel", "enclosure"));

            elem.Add(new XAttribute(NoNamespace + "href", ao["url"].First().Primitive));

            if (ao["fileSize"].Any())
                elem.Add(new XAttribute(NoNamespace + "length", ao["fileSize"].First().Primitive));

            if (ao["mediaType"].Any())
                elem.Add(new XAttribute(NoNamespace + "type", ao["mediaType"].First().Primitive));

            return elem;
        }

        private async Task _buildActivityObject(XElement elem, ASObject ao, string mainActor, bool sub)
        {
            var idval = ao.Id;
            if (_entityConfiguration.IsActivity(ao))
            {
                await _buildActivity(elem, ao, mainActor);
                return;
            }

            if (_entityConfiguration.IsActor(ao))
            {
                foreach (var item in _createAuthor(ao).Descendants())
                    elem.Add(item);
                return;
            }
            if (idval.EndsWith("#object") && (string)ao["_:origin"].FirstOrDefault()?.Primitive == "atom") idval = idval.Substring(0, idval.Length - 7);
            if (!sub)
                elem.Add(new XElement(Atom + "id", idval));

            var objectType = ao.Type.First();
            if (VerbTranslation.ContainsKey(objectType)) objectType = VerbTranslation[objectType];
            elem.Add(new XElement(ActivityStreams + "object-type", _typeToObjectType(objectType, ao["inReplyTo"].Any())));

            // Mastodon content warning
            if (ao["summary"].Any())
                elem.Add(new XElement(Atom + "summary", ao["summary"].First().Primitive));

            if (!sub)
            {
                if (ao["published"].Any())
                    elem.Add(new XElement(Atom + "published", ao["published"].First().Primitive));
                if (ao["updated"].Any())
                    elem.Add(new XElement(Atom + "updated", ao["updated"].First().Primitive));
            }

            if (ao["content"].Any())
                elem.Add(new XElement(Atom + "content",
                    new XAttribute(NoNamespace + "type", ao["mediaType"].FirstOrDefault()?.Primitive ?? "html"),
                    ao["content"].First().Primitive));

            if (ao["_:conversation"].Any())
                elem.Add(new XElement(OStatus + "conversation", new XAttribute(NoNamespace + "ref", ao["_:conversation"].First().Primitive)));

            if (ao["inReplyTo"].Any())
                elem.Add(new XElement(AtomThreading + "in-reply-to", new XAttribute(NoNamespace + "ref", (await _fixupPointing(ao["inReplyTo"].First())).Id)));

            foreach (var tag in ao["tag"])
            {
                var obj = tag.Id != null ? (await _get(tag.Id)) : tag.SubObject;
                if (obj.Type.Contains("https://www.w3.org/ns/activitystreams#Hashtag"))
                {
                    var hashtag = ((string)obj["name"].First().Primitive).Replace("#", "");

                    elem.Add(new XElement(Atom + "category", new XAttribute(NoNamespace + "term", hashtag)));
                }
            }

            if (!sub)
            {
                if ((string) ao["_:origin"].FirstOrDefault()?.Primitive == "atom" || idval.StartsWith("tag:"))
                    elem.Add(
                        new XElement(Atom + "link",
                        new XAttribute(NoNamespace + "rel", "alternate"),
                        new XAttribute(NoNamespace + "type", "text/html"),
                        new XAttribute(NoNamespace + "href", ao["url"].First().Primitive)));
                else
                    elem.Add(
                        new XElement(Atom + "link",
                            new XAttribute(NoNamespace + "rel", "alternate"),
                            new XAttribute(NoNamespace + "type", "text/html"),
                            new XAttribute(NoNamespace + "href", ao.Id)));
            }

            if ((string)ao["_:origin"].FirstOrDefault()?.Primitive != "atom")
                elem.Add(new XElement(Atom + "link",
                    new XAttribute(NoNamespace + "rel", sub ? "object" : "self"),
                    new XAttribute(NoNamespace + "type",
                        "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\""),
                    new XAttribute(NoNamespace + "href", ao.Id)));

            foreach (var attachment in ao["attachment"])
            {
                elem.Add(_buildAttachment(attachment.Id == null ? attachment.SubObject : await _get(attachment.Id)));
            }

            foreach (var target in ao["to"])
                elem.Add(_buildMention(target));
        }

        private async Task<APEntity> _fixupPointing(ASTerm term)
        {
            var id = term.Id;
            var entity = await _entityStore.GetEntity(id, false);
            if (entity == null) return null;

            if (_entityConfiguration.IsActivity(entity.Data)) return entity;

            var target = await _activityService.DetermineOriginatingCreate(id);
            return target;
        }

        private async Task<XElement> _buildActivity(XElement elem, ASObject ao, string mainActor, bool isRoot = false)
        {
            if (isRoot) _setNamespaces(elem);
            var idval = ao.Id;
            var verb = ao.Type.First();
            if (VerbTranslation.ContainsKey(verb)) verb = VerbTranslation[verb];

            var targetObject = ao["object"].FirstOrDefault();
            if (verb == "https://www.w3.org/ns/activitystreams#Undo" && targetObject != null)
            {
                var toUndo = await _get(targetObject.Id);
                targetObject = toUndo["object"].First();
                if (toUndo.Type.Contains("https://www.w3.org/ns/activitystreams#Like")) verb = "Unfavorite";
                if (toUndo.Type.Contains("https://www.w3.org/ns/activitystreams#Follow")) verb = "Unfollow";
            }
            elem.Add(new XElement(ActivityStreams + "verb", _typeToObjectType(verb)));

            elem.Add(new XElement(Atom + "id", idval));

            if (ao["summary"].Any())
                elem.Add(new XElement(Atom + "title", ao["summary"].First().Primitive));
            if (ao["published"].Any())
                elem.Add(new XElement(Atom + "published", ao["published"].First().Primitive));
            if (ao["updated"].Any())
                elem.Add(new XElement(Atom + "updated", ao["updated"].First().Primitive));

            if (ao["actor"].Any(a => a.Id != mainActor))
            {
                var author = await _get(ao["actor"].First(a => a.Id != mainActor).Id);

                elem.Add(_createAuthor(author));
            }

            var self = ao["atomUri"].Select(a => (string) a.Primitive).Concat(new[] { ao.Id }).First();

            if ((string)ao["_:origin"].FirstOrDefault()?.Primitive == "atom")
            {
                // if no direct link to the activity (only a sublink), don't make self
                if (ao["url"].Any() && self != (string)ao["url"].First().Primitive)
                    elem.Add(
                        new XElement(Atom + "link",
                            new XAttribute(NoNamespace + "rel", "self"),
                            new XAttribute(NoNamespace + "type", "application/atom+xml"),
                            new XAttribute(NoNamespace + "href", self)),
                        new XElement(Atom + "link",
                            new XAttribute(NoNamespace + "rel", "alternate"),
                            new XAttribute(NoNamespace + "type", "text/html"),
                            new XAttribute(NoNamespace + "href", ao["url"].First().Primitive)));
            }
            else
            {
                elem.Add(
                    new XElement(Atom + "link",
                        new XAttribute(NoNamespace + "rel", "alternate"),
                        new XAttribute(NoNamespace + "type", "application/json+ld; profile=\"https://www.w3.org/ns/activitystreams\""),
                        new XAttribute(NoNamespace + "href", self)),
                    new XElement(Atom + "link",
                        new XAttribute(NoNamespace + "rel", "alternate"),
                        new XAttribute(NoNamespace + "type", "text/html"),
                        new XAttribute(NoNamespace + "href", ao.Id)),
                    new XElement(Atom + "link",
                        new XAttribute(NoNamespace + "rel", "self"),
                        new XAttribute(NoNamespace + "type", "application/atom+xml"),
                        new XAttribute(NoNamespace + "href", _makeAtomUrl(ao.Id))));
            }

            if (targetObject == null) return elem;

            if (verb == "Post")
            {
                await _buildActivityObject(elem, await _get(targetObject.Id), mainActor, true);
            }
            else
            {
                var e = new XElement(ActivityStreams + "object");
                ASObject obj;
                if (verb == "Share" || verb == "Favorite" || verb == "Unfavorite")
                    obj = (await _fixupPointing(targetObject)).Data;
                else
                    obj = await _get(targetObject.Id);
                await _buildActivityObject(e, obj, mainActor, false);
                elem.Add(e);

                elem.Add(new XElement(ActivityStreams + "object-type", _typeToObjectType("activity")));

                foreach (var target in ao["to"])
                    elem.Add(_buildMention(target));

                if (verb == "Share")
                    elem.Add(e.Descendants(Atom + "content").First());
            }

            if (elem.Element(Atom + "content") == null)
                elem.Add(new XElement(Atom + "content", "[empty]"));

            return elem;
        }

        private async Task<ASObject> _get(object id)
        {
            return (await _entityStore.GetEntity((string)id, false)).Data;
        }

        private async Task<ASObject> _get(ASTerm id)
        {
            if (id.Id != null)
                return (await _entityStore.GetEntity((string)id.Id, false)).Data;
            return id.SubObject;
        }

        private async Task<XElement> _buildFeed(ASObject ao)
        {
            var elem = new XElement(Atom + "feed");
            _setNamespaces(elem);

            elem.Add(new XElement(Atom + "id", ao.Id));

            if (ao["attributedTo"].Any())
                elem.Add(_createAuthor(await _get(ao["attributedTo"].First())));

            if (ao["summary"].Any())
                elem.Add(new XElement(Atom + "title", ao["summary"].First().Primitive));

            if (ao["summary"].Any())
                elem.Add(new XElement(Atom + "subtitle", ao["summary"].First().Primitive));

            elem.Add(new XElement(Atom + "updated", ao["updated"].Any() ? ao["updated"].First().Primitive : DateTime.Now.ToString("o")));

            var isNativeAtom = ao["_:origin"].Any(a => (string)a.Primitive == "atom");

            elem.Add(new XElement(Atom + "link",
                new XAttribute(NoNamespace + "rel", "self"),
                new XAttribute(NoNamespace + "type", "application/atom+xml"),
                new XAttribute(NoNamespace + "href", (isNativeAtom ? ao.Id : _makeAtomUrl(ao.Id)))));

            if (!isNativeAtom)
                elem.Add(
                    new XElement(Atom + "link",
                        new XAttribute(NoNamespace + "rel", "alternate"),
                        new XAttribute(NoNamespace + "type", "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\""),
                        new XAttribute(NoNamespace + "href", ao.Id)),
                    new XElement(Atom + "link",
                        new XAttribute(NoNamespace + "rel", "alternate"),
                        new XAttribute(NoNamespace + "type", "text/html"),
                        new XAttribute(NoNamespace + "href", ao.Id)));

            foreach (var url in ao["url"])
                elem.Add(new XElement(Atom + "link",
                    new XAttribute(NoNamespace + "rel", "alternate"),
                    new XAttribute(NoNamespace + "type", "text/html"),
                    new XAttribute(NoNamespace + "href", url.Primitive)));

            if (ao["_:salmonUrl"].Any())
                elem.Add(new XElement(Atom + "link",
                    new XAttribute(NoNamespace + "rel", "salmon"),
                    new XAttribute(NoNamespace + "href", ao["_:salmonUrl"].First().Primitive)));
            else if (!isNativeAtom)
            {
                var inbox = (string) (await _get(ao["attributedTo"].First().Id))["inbox"].First().Id;

                elem.Add(new XElement(Atom + "link",
                    new XAttribute(NoNamespace + "rel", "salmon"),
                    new XAttribute(NoNamespace + "href", inbox)));
            }

            if (ao["_:hubUrl"].Any())
                elem.Add(new XElement(Atom + "link",
                    new XAttribute(NoNamespace + "rel", "hub"),
                    new XAttribute(NoNamespace + "href", ao["_:hubUrl"].First().Primitive)));
            else if (!isNativeAtom)
            {
                elem.Add(new XElement(Atom + "link",
                    new XAttribute(NoNamespace + "rel", "hub"),
                    new XAttribute(NoNamespace + "href", ao["attributedTo"].First().Id + "?hub")));
            }

            if (ao["next"].Any())
                elem.Add(new XElement(Atom + "link",
                    new XAttribute(NoNamespace + "rel", "next"),
                    new XAttribute(NoNamespace + "type", "application/atom+xml"),
                    new XAttribute(NoNamespace + "href", isNativeAtom ? ao["next"].First().Id : _makeAtomUrl((string)ao["next"].First().Primitive))));

            if (ao["prev"].Any())
                elem.Add(new XElement(Atom + "link",
                    new XAttribute(NoNamespace + "rel", "prev"),
                    new XAttribute(NoNamespace + "type", "application/atom+xml"),
                    new XAttribute(NoNamespace + "href", isNativeAtom ? ao["prev"].First().Id : _makeAtomUrl((string)ao["prev"].First().Primitive))));

            foreach (var item in ao["orderedItems"])
            {
                var e = new XElement(Atom + "entry");
                await _buildActivity(e, await _get(item.Id), (string) ao["attributedTo"].FirstOrDefault()?.Primitive);
                elem.Add(e);
            }

            return elem;
        }

        public AtomEntryGenerator(IEntityStore entityStore, ActivityService activityService, EntityData entityConfiguration)
        {
            _entityStore = entityStore;
            _activityService = activityService;
            _entityConfiguration = entityConfiguration;
        }

        public async Task<XDocument> Build(ASObject ao, IEntityStore newStore = null)
        {
            var tmpOldStore = _entityStore;
            _entityStore = newStore ?? _entityStore;
            XElement e;
            if (ao.Type.Contains("https://www.w3.org/ns/activitystreams#OrderedCollectionPage"))
                e = await _buildFeed(ao);
            else
            {
                e = new XElement(Atom + "entry");
                await _buildActivity(e, ao, null, true);
            }

            _entityStore = tmpOldStore;
            return new XDocument(e);
        }
    }
}
