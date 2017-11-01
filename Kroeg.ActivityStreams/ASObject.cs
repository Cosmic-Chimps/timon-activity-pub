using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kroeg.ActivityStreams
{
    public class ASObject : IEnumerable<KeyValuePair<string, List<ASTerm>>>
    {
        private Dictionary<string, List<ASTerm>> _terms = new Dictionary<string, List<ASTerm>>();
        public List<string> Type { get; } = new List<string>();
        public string Id { get; set; }

        private static JToken _context = "https://www.w3.org/ns/activitystreams";

        private static Dictionary<string, JObject> _objectStore = new Dictionary<string, JObject>();
        private static JsonLD.API _api = new JsonLD.API(_resolve);
        private static string _contextUrl;

        public List<string> CompactedTypes => Type.Select(_ldContext.CompactIRI).ToList();
        public static async Task SetContext(JToken context, string contextUrl)
        {
            _context = context;
            _api = new JsonLD.API(_resolve);
            _ldContext = await _api.BuildContext(context);
            _contextUrl = contextUrl;
        }

        private static async Task<JObject> _resolve(string uri)
        {
            if (_objectStore.ContainsKey(uri)) return _objectStore[uri];

            var hc = new HttpClient();
            hc.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/ld+json"));

            return _objectStore[uri] = JObject.Parse(await hc.GetStringAsync(uri));
        }

        private static JsonLD.Context _ldContext = null;

        public List<ASTerm> this[string value] {
            get {
                var url = _ldContext.ExpandIRI(value);
                if (url == "@id") throw new NotSupportedException("Can't get ID this way anymore!");
                if (url == "@type") throw new NotSupportedException("Can't get Type this way anymore!");

                return _terms.ContainsKey(url) ? _terms[url] : (_terms[url] = new List<ASTerm>());
            }
        }

        public void Replace(string key, ASTerm value)
        {
            var url = _ldContext.ExpandIRI(key);
            if (url == "@id" || url == "@type") throw new NotSupportedException("no. don't.");

            _terms[url] = new List<ASTerm> { value };
        }

        public static ASObject Parse(string obj, bool impliedContext = false)
        {
            var ser = new JsonTextReader(new StringReader(obj));
            ser.DateParseHandling = DateParseHandling.None;
            var jobj = JObject.Load(ser);
            if (impliedContext && jobj["@context"] == null)
            {
                if (_context.Type != JTokenType.Array)
                    jobj["@context"] = new JArray("https://www.w3.org/ns/activitystreams", _context);
                else
                {
                    var narr = new JArray("https://www.w3.org/ns/activitystreams");
                    foreach (var item in (JArray) _context)
                        narr.Add(item);

                    jobj["@context"] = narr;
                }
            }
            return Parse(_api.Expand(jobj).Result);
        }

        public static ASObject Parse(JToken obj) {
            if (obj.Type != JTokenType.Object) return null;
            
            var a = new ASObject();
            foreach (var kv in (JObject) obj) {
                if (kv.Key == "@type")
                    a.Type.AddRange(kv.Value.Select(b => b.ToObject<string>()));
                else if (kv.Key == "@id")
                    a.Id = kv.Value.ToObject<string>();
                else
                {
                    if (((JArray)kv.Value).Count == 1) {
                        var ar = (JObject) ((JArray) kv.Value)[0];
                        if (ar["@list"] != null) {
                            a._terms.Add(kv.Key, (ar["@list"]).Select(b => ASTerm.Parse((JObject) b)).ToList());
                            continue;
                        }
                    }
                    a._terms.Add(kv.Key, kv.Value.Select(b => ASTerm.Parse((JObject) b)).ToList());
                }
            }

            return a;
        }

        public ASObject Clone()
        {
            var o = new ASObject();
            foreach (var kv in _terms)
            {
                o._terms[kv.Key] = new List<ASTerm>(kv.Value.Select(a => a.Clone()));
            }

            return o;
        }

        public JObject Serialize(bool addContext = false, bool compact = true)
        {
            var newObject = new JObject();
            if (Id != null) newObject["@id"] = Id;
            if (Type.Count > 0)
                newObject["@type"] = new JArray(Type);
            foreach (var kv in _terms)
                if (kv.Value.Count == 0)
                    continue;
                else if (_ldContext.TermDefinitions.Any(a => a.Value.IriMapping == kv.Key && a.Value.ContainerMapping == "@list"))
                    newObject[kv.Key] = new JArray(new JObject { ["@list"] = new JArray(kv.Value.Select(a => a.Serialize(false)).ToArray()) });
                else
                    newObject[kv.Key] = new JArray(kv.Value.Select(a => a.Serialize(false)).ToArray());

            if (compact)
            {
                newObject = (JObject) _api.CompactExpanded(_ldContext, newObject);
                if (addContext) newObject["@context"] = new JArray("https://www.w3.org/ns/activitystreams", _contextUrl);
            }

            return newObject;
        }

        public IEnumerator<KeyValuePair<string, List<ASTerm>>> GetEnumerator()
        {
            return _terms.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _terms.GetEnumerator();
        }
    }
}
