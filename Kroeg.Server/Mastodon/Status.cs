using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Kroeg.Server.Mastodon
{
    public class Context
    {
        [JsonProperty("ancestors")] public List<Status> ancestors { get; set; }
        [JsonProperty("descendants")] public List<Status> descendants { get; set; }
    }

    public class Mention
    {
        [JsonProperty("url")] public string url { get; set; }
        [JsonProperty("username")] public string username { get; set; }
        [JsonProperty("acct")] public string acct { get; set; }
        [JsonProperty("id")] public string id { get; set; }
    }

    public class AttachmentMeta
    {
        [JsonProperty("width")] public int width { get; set; }
        [JsonProperty("height")] public int height { get; set; }
        [JsonProperty("size")] public int size { get; set; }
        [JsonProperty("aspect")] public int aspect { get; set; }
    }

    public class Attachment
    {
        [JsonProperty("id")] public string id { get; set; }
        [JsonProperty("type")] public string type { get; set; }
        [JsonProperty("url")] public string url { get; set; }
        [JsonProperty("remote_url")] public string remote_url { get; set; }
        [JsonProperty("preview_url")] public string preview_url { get; set; }
        [JsonProperty("text_url")] public string text_url { get; set; }
        [JsonProperty("description")] public string description { get; set; }
        [JsonProperty("meta")] public Dictionary<string, AttachmentMeta> meta { get; set; }
    }

    public class Emoji
    {
        [JsonProperty("shortcode")] public string shortcode { get; set; }
        [JsonProperty("static_url")] public string static_url { get; set; }
        [JsonProperty("url")] public string url { get; set; }
    }

    public class Tag
    {
        [JsonProperty("name")] public string name { get; set; }
        [JsonProperty("url")] public string url { get; set; }
    }

    public class Status
    {
        [JsonProperty("id")] public string id { get; set; }
        [JsonProperty("uri")] public string uri { get; set; }
        [JsonProperty("url")] public string url { get; set; }
        [JsonProperty("account")] public Account account { get; set; }
        [JsonProperty("in_reply_to_id")] public string in_reply_to_id { get; set; }
        [JsonProperty("in_reply_to_account_id")] public string in_reply_to_account_id { get; set; }
        [JsonProperty("reblog")] public Status reblog { get; set; }
        [JsonProperty("content")] public string content { get; set; }
        [JsonProperty("created_at")] public DateTime created_at { get; set; }
        [JsonProperty("emojis")] public List<Emoji> emojis { get; set; }
        [JsonProperty("reblogs_count")] public int reblogs_count { get; set; }
        [JsonProperty("favourites_count")] public int favourites_count { get; set; }
        [JsonProperty("reblogged")] public bool reblogged { get; set; }
        [JsonProperty("favourited")] public bool favourited { get; set; }
        [JsonProperty("muted")] public bool muted { get; set; }
        [JsonProperty("sensitive")] public bool sensitive { get; set; }
        [JsonProperty("spoiler_text")] public string spoiler_text { get; set; }
        [JsonProperty("visibility")] public string visibility { get; set; }
        [JsonProperty("media_attachments")] public List<Attachment> media_attachments { get; set; }
        [JsonProperty("mentions")] public List<Mention> mentions { get; set; }
        [JsonProperty("tags")] public List<Tag> tags { get; set; }
        [JsonProperty("application")] public Application application { get; set; }
        [JsonProperty("language")] public string language { get; set; }
        [JsonProperty("pinned")] public bool pinned { get; set; }
    }
}
