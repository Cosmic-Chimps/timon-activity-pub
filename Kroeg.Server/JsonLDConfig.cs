using Newtonsoft.Json.Linq;

namespace Kroeg.Server
{
  public class JsonLDConfig
  {
    public static JArray GetContext(bool full)
    {
      var data = new JArray(
          "https://w3id.org/security/v1",
          new JObject
          { // stuff Mastodon defines:
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

          new JObject
          { // Kroeg-y specific-ish
                  ["jwks"] = new JObject { ["@id"] = "as:jwks", ["@type"] = "@id" },
            ["uploadMedia"] = new JObject { ["@id"] = "as:uploadMedia", ["@type"] = "@id" },
            ["likes"] = new JObject { ["@id"] = "as:likes", ["@type"] = "@id" },
            ["liked"] = new JObject { ["@id"] = "as:liked", ["@type"] = "@id" },
            ["shares"] = new JObject { ["@id"] = "as:shares", ["@type"] = "@id" },

            ["kroeg"] = "https://timon.com/kroeg/ns#",
            ["unlisted"] = "kroeg:unlisted",
            ["settingsEndpoint"] = new JObject { ["@id"] = "kroeg:settingsEndpoint", ["@type"] = "@id" },
            ["relevantObjects"] = new JObject { ["@id"] = "kroeg:relevantObjects", ["@type"] = "@id" },
            ["search"] = new JObject { ["@id"] = "kroeg:search", ["@type"] = "@id" },
            ["blocks"] = new JObject { ["@id"] = "kroeg:blocks", ["@type"] = "@id" },
            ["blocked"] = new JObject { ["@id"] = "kroeg:blocked", ["@type"] = "@id" },

            ["c2s"] = "https://timon.com/kroeg/c2s#",
            ["c2s:likes"] = new JObject { ["@type"] = "@id" },
            ["c2s:follows"] = new JObject { ["@type"] = "@id" },
            ["c2s:announces"] = new JObject { ["@type"] = "@id" },
            ["c2s:accepts"] = new JObject { ["@type"] = "@id" },
            ["c2s:rejects"] = new JObject { ["@type"] = "@id" },
            ["c2s:replies"] = new JObject { ["@type"] = "@id" },
          }
      );

      if (full)
        data.Insert(0, "https://www.w3.org/ns/activitystreams");
      return data;
    }
  }
}
