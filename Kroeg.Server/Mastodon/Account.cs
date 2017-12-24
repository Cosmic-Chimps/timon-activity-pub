using System;
using Newtonsoft.Json;

namespace Kroeg.Server.Mastodon
{
    public class Account
    {
		public class Source
		{
			[JsonProperty("privacy")] public string privacy { get; set; }
			[JsonProperty("sensitive")] public bool sensitive { get; set; }
			[JsonProperty("note")] public string note { get; set; }
		}

		[JsonProperty("id")] public string id { get; set; }
		[JsonProperty("username")] public string username { get; set; }
		[JsonProperty("acct")] public string acct { get; set; }
		[JsonProperty("display_name")] public string display_name { get; set; }
		[JsonProperty("locked")] public bool locked { get; set; }
		[JsonProperty("created_at")] public DateTime created_at { get; set; }
		[JsonProperty("followers_count")] public int followers_count { get; set; }
		[JsonProperty("following_count")] public int following_count { get; set; }
		[JsonProperty("statuses_count")] public int statuses_count { get; set; }
		[JsonProperty("note")] public string note { get; set; }
		[JsonProperty("url")] public string url { get; set; }
		[JsonProperty("avatar")] public string avatar { get; set; }
		[JsonProperty("avatar_static")] public string avatar_static { get; set; }
		[JsonProperty("header")] public string header { get; set; }
		[JsonProperty("header_static")] public string header_static { get; set; }
		[JsonProperty("moved")] public Account moved { get; set; }
		[JsonProperty("source")] public Source source { get; set; }
    }
}