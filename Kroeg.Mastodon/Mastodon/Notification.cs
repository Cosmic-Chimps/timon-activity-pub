using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Kroeg.Mastodon
{
    public class Notification
    {
        [JsonProperty("id")] public string id { get; set; }
        [JsonProperty("type")] public string type { get; set; }
        [JsonProperty("created_at")] public DateTime created_at { get; set; }
        [JsonProperty("account")] public Account account { get; set; }
        [JsonProperty("status")] public Status status { get; set; }
    }
}