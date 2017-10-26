using Newtonsoft.Json.Linq;

namespace Kroeg.Server {
    public class JsonLDConfig {
        public static JArray Context => new JArray(
            "https://www.w3.org/ns/activitystreams-history/v1.8",
            "https://w3id.org/security/v1",
            new JObject { // stuff Mastodon defines:
                // AS2 extensions
                ["manuallyApprovesFollowers"] = "as:manuallyApprovesFollowers",
                ["sensitive"] = "as:sensitive",
                ["Hashtag"] = "as:Hashtag",

                // OStatus compat
                ["ostatus"] = "http://ostatus.org#",
                ["atomUri"] = "ostatus:atomUri",
                ["inReplyToAtomUri"] = "ostatus:inReplyToAtomUri",
                ["conversation"] = "ostatus:conversation",

                // Mastodon thingys
                ["toot"] = "http://joinmastodon.org/ns#",
                ["Emoji"] = "toot:Emoji"
            },

            new JObject { // Kroeg-y specific-ish
                ["jwks"] = new JObject { ["@id"] = "as:jwks", ["@type"] = "@id" },
                ["uploadMedia"] = new JObject { ["@id"] = "as:uploadMedia", ["@type"] = "@id" },

                ["kroeg"] = "https://puckipedia.com/kroeg/ns#",
                ["settingsEndpoint"] = "kroeg:settingsEndpoint",
                ["relevantObjects"] = "kroeg:relevantObjects",
            }
        );
    }
}