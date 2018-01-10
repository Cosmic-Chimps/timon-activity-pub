using Newtonsoft.Json;

namespace Kroeg.Mastodon
{
    public class Application
    {
        public class Request
        {
            public string client_name { get; set; }
            public string redirect_urls { get; set; }
            public string scopes { get; set; }
            public string website { get; set; }
        }

        public class Response
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("client_id")]
            public string ClientId { get; set; }

            [JsonProperty("client_secret")]
            public string ClientSecret { get; set; }
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("website")]
        public string Website { get; set; }
    }
}