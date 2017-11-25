using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Jint;
using Kroeg.ActivityStreams;
using HtmlAgilityPack;
using Jint.Parser.Ast;
using System.Text.RegularExpressions;

namespace Kroeg.Server.Services.Template
{
    public class TemplateService
    {
        public Dictionary<string, TemplateItem> Templates { get; } = new Dictionary<string, TemplateItem>();

        private string _base = "templates/";
        private string _baseOverride = "template_override/";

        public string PageTemplate { get; private set; }

        public class Registers {
            public Engine Engine { get; set; }
            public ASHandler Handler { get; set; }
            public RendererData Renderer { get; set; }
            public Dictionary<string, APEntity> UsedEntities { get; set; } = new Dictionary<string, APEntity>();
            public Dictionary<string, string> Data { get; set; } = new Dictionary<string, string>();
            public int EntityCount { get; set; }
        }

        public class ASHandler
        {
            public ASObject obj { get; set; }

            public object[] get(string name)
            {
                if (name == "id")
                    if (obj.Id != null) return new object[] { obj.Id };
                    else return new object[] {};
                if (name == "type") return obj.CompactedTypes.ToArray();
                return obj[name].Select(a => a.Id ?? a.Primitive ?? a.SubObject).ToArray();
            }

            public object take(string name, object def)
            {
                return get(name).FirstOrDefault() ?? def;
            }

            public object take(string name)
            {
                return get(name).FirstOrDefault() ?? "";
            }

            public bool has(string name)
            {
                return get(name).Length > 0;
            }

            public bool contains(string name, object val)
            {
                return get(name).Any(a => a is string && (string) a == (string) val);
            }

            public bool containsAny(string name, object[] vals)
            {
                var check = get(name);
                return vals.Any(a => a is string && check.Any(b => (string) b == (string) a));
            }
        }

        private class doRender {
            public string Template { get; set; }
            public string RenderID { get; set; }
        }

        public class RendererData
        {
            private static Regex _allowed_attributes = new Regex("^(href|rel|class)$");
            private static Regex _allowed_classes = new Regex("^((h|p|u|dt|e)-.*|mention|hashtag|ellipsis|invisible)$");
            private static Regex _disallowed_nodes = new Regex("^(script|object|embed)$");

            public Dictionary<string, string> EmojiContext = new Dictionary<string, string>();

            public string date(string data)
            {
                try {
                    return DateTime.Parse(data).ToLocalTime().ToString();
                } catch (FormatException) { return data; }
            }

            public string sanitize(string data)
            {
                return data.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            }

            private static Regex _emojiRegex = new Regex("([^\\w:]|\n|^):([a-zA-Z0-9_]{2,}):([^\\w:]|$)");

            private void _clean(HtmlNode node)
            {
                if (node.NodeType == HtmlNodeType.Text)
                {
                    var tnode = (HtmlTextNode) node;
                    Match m = Match.Empty;
                    while ((m = _emojiRegex.Match(tnode.Text)).Success)
                    {
                        var first = tnode.Text.Substring(0, m.Index + m.Groups[1].Length);
                        var name = $":{m.Groups[2].Value}:";
                        var second = tnode.Text.Substring(m.Groups[3].Index);

                        tnode.Text = first;
                        if (EmojiContext.ContainsKey(name))
                        {
                            var url = EmojiContext[name];
                            var im = node.OwnerDocument.CreateElement("img");
                            im.Attributes.Add("src", url);
                            im.Attributes.Add("alt", name);
                            im.Attributes.Add("title", name);
                            im.AddClass("emoji");
                            tnode.ParentNode.InsertAfter(im, tnode);
                            
                            tnode = node.OwnerDocument.CreateTextNode();
                            tnode.Text = second;
                            im.ParentNode.InsertAfter(tnode, im);
                        }
                        else
                        {
                            var im = node.OwnerDocument.CreateElement("span");
                            im.AppendChild(node.OwnerDocument.CreateTextNode(name));
                            tnode.ParentNode.InsertAfter(im, tnode);
                            
                            tnode = node.OwnerDocument.CreateTextNode();
                            tnode.Text = second;
                            im.ParentNode.InsertAfter(tnode, im);
                        }
                    }
                }
                if (node.NodeType != HtmlNodeType.Element && node.NodeType != HtmlNodeType.Document) return;
                if (node.NodeType == HtmlNodeType.Element)
                {
                    if (_disallowed_nodes.IsMatch(node.Name.ToLower()))
                    {
                        node.Remove();
                        return;
                    }

                    foreach (var attribute in node.Attributes.ToArray())
                    {
                        if (!_allowed_attributes.IsMatch(attribute.Name)) node.Attributes.Remove(attribute);
                    }

                    if (node.Attributes.Contains("class"))
                    foreach (var cl in node.Attributes["class"].Value.Split(' '))
                    {
                        if (cl.Length > 0 && !_allowed_classes.IsMatch(cl)) node.RemoveClass(cl);
                    }
                }

                foreach (var child in node.ChildNodes.ToArray()) {
                    _clean(child);
                }
            }

            public string clean(string data)
            {
                if (string.IsNullOrWhiteSpace(data)) return "";

                var doc = new HtmlDocument();
                doc.LoadHtml(data);
                _clean(doc.DocumentNode);
                return doc.DocumentNode.InnerHtml;
            }

            public bool server => true;
            public bool client => false;
        }

        public TemplateService()
        {
            Parse();
        }

        public void Parse()
        {
            Templates.Clear();
            _parse(_base);
            _parse(_baseOverride);
        }

        private void _parse(string dir)
        {
            foreach (var file in Directory.EnumerateFiles(dir))
                _parseFile(file);

            foreach (var subdir in Directory.EnumerateDirectories(dir))
                _parse(subdir);
        }

        private void _parseFile(string path)
        {
            if (path == "template_override/.keep") return;

            var data = File.ReadAllText(path);
            if (path == "templates/page.html") PageTemplate = data;
            var templateName = path.Substring(path.IndexOf("/") + 1, path.Length - path.IndexOf("/") - 6).Replace('\\', '/');
            Templates[templateName] = TemplateParser.Parse(data);
        }

        private object _parse(string data, ASObject obj, Registers regs)
        {
            var engine = regs.Engine;
            regs.Handler.obj = obj;

            return regs.Engine.Execute(data).GetCompletionValue().ToObject();
        }

        private async Task<string> _parseElement(HtmlDocument doc, TemplateItem item, IEntityStore entityStore, ASObject data, Registers regs)
        {
            regs.Engine.SetValue("Data", regs.Data);
            regs.Renderer.EmojiContext.Clear();
            var result = doc.CreateElement(item.Data);
            var extraRenderData = new Dictionary<string, string>(regs.Data);
            foreach (var argument in item.Arguments)
            {
                if (!argument.Key.StartsWith("x-") || (argument.Key.StartsWith("x-render-") && argument.Key != "x-render-id"))
                {
                    var resultValue = new StringBuilder();
                    foreach (var subitem in argument.Value)
                        if (subitem.Type == TemplateItemType.Text)
                            resultValue.Append(subitem.Data);
                        else
                            resultValue.Append(_parse(subitem.Data, data, regs));
                    if (argument.Key.StartsWith("x-render-"))
                        extraRenderData[argument.Key.Replace("x-render-", "")] = resultValue.ToString();
                    else
                        result.Attributes.Add(argument.Key, resultValue.ToString());
                }
            }

            bool parse = true;

            if (item.Arguments.ContainsKey("x-if"))
                parse = (bool) _parse(item.Arguments["x-if"][0].Data, data, regs);
            else if (item.Arguments.ContainsKey("x-else"))
                parse = !(bool) _parse(item.Arguments["x-else"][0].Data, data, regs);

            if (!parse) return "";

            if (item.Arguments.ContainsKey("x-render-if"))
                parse = (bool) _parse(item.Arguments["x-render-if"][0].Data, data, regs);

            string err = "";
            
            if ((item.Arguments.ContainsKey("x-render-id") || item.Arguments.ContainsKey("x-render")) && parse)
            {
                var template = item.Arguments.ContainsKey("x-render") ? item.Arguments["x-render"][0].Data : null;
                string id = null;
                ASObject objData = null;
                if (item.Arguments.ContainsKey("x-render-id")) 
                {
                    var res = _parse(item.Arguments["x-render-id"][0].Data, data, regs);
                    if (res is ASObject) objData = (ASObject) res;
                    else id = (string) res;
                }
                else
                    id = data.Id;

                if (objData == null && data.Id == id)
                    objData = data;
                else if (objData == null && id != null)
                {
                    try {

                        APEntity obj = null;
                        if (regs.UsedEntities.ContainsKey(id))
                            obj = regs.UsedEntities[id];
                        else
                            try {
                                obj = await entityStore.GetEntity(id, true);
                            } catch (Exception) { /* nom */ }

                        if (obj != null)
                        {
                            regs.UsedEntities[id] = obj;
                            objData = obj.Data;
                        }

                        regs.EntityCount++;
                    } catch (InvalidOperationException) {
                        err = $"<!-- {id} welp -->";
                    }
                }

                if (objData != null) {
                    var oldData = regs.Data;
                    regs.Data = extraRenderData;
                    string r;
                    if (template != null)
                        r = await _parseTemplate(template, entityStore, objData, regs, doc);
                    else
                        r = await _parseElement(doc, item, entityStore, objData, regs);
                    regs.Data = oldData;
                    return r;
                }
            }
            
            if (item.Arguments.ContainsKey("data-component") && item.Arguments["data-component"][0].Data == "renderhost" && parse)
            {
                var template = item.Arguments["data-template"][0].Data;
                string id = null;
                ASObject objData = null;

                var res = _parse(item.Arguments["data-id"][0].Data, data, regs);
                if (res is ASObject) objData = (ASObject) res;
                else id = (string) res;

                if (objData == null && data.Id == id)
                    objData = data;
                else if (objData == null)
                {
                    APEntity obj;
                    if (regs.UsedEntities.ContainsKey(id))
                        obj = regs.UsedEntities[id];
                    else
                        obj = await entityStore.GetEntity(id, true);

                    if (obj != null)
                    {
                        regs.UsedEntities[id] = obj;
                        objData = obj.Data;
                    }

                    regs.EntityCount++;
                }

                if (objData != null)
                    return await _parseTemplate(template, entityStore, objData, regs, doc);
            }

            if (item.Arguments.ContainsKey("data-component") && item.Arguments["data-component"][0].Data == "emoji" && parse)
            {
                foreach (var tagitem in data["tag"]) {
                    var obj = tagitem.SubObject;
                    if (obj == null)
                    {
                        var entity = await entityStore.GetEntity(tagitem.Id, true);
                        if (entity != null)
                        {
                            regs.UsedEntities[entity.Id] = entity;
                            obj = entity.Data;
                        }
                    }

                    if (obj == null || !obj.Type.Contains("http://joinmastodon.org/ns#Emoji")) continue;

                    var emojiName = (string) obj["name"].First().Primitive;
                    var url = obj["icon"].First().SubObject;

                    if (url == null)
                    {
                        var entity = await entityStore.GetEntity(obj["icon"].First().Id, true);
                        if (entity != null)
                        {
                            regs.UsedEntities[entity.Id] = entity;
                            url = entity.Data;
                        }
                    }

                    if (url == null) continue;

                    regs.Renderer.EmojiContext[emojiName] = url["url"].First().Id;
                }
            }

            var content = new StringBuilder();

            foreach (var subitem in item.Children)
            {
                if (subitem.Type == TemplateItemType.Text)
                    content.Append(subitem.Data);
                else if (subitem.Type == TemplateItemType.Script)
                    content.Append((string) _parse(subitem.Data, data, regs));
                else if (subitem.Type == TemplateItemType.Element)
                {
                    if (subitem.Arguments.ContainsKey("x-for-in"))
                    {
                        var forItems = (object[]) _parse(subitem.Arguments["x-for-in"][0].Data, data, regs);
                        var forIn = "item";
                        foreach (var forItem in forItems)
                        {
                            regs.Engine.SetValue(forIn, forItem);
                            content.Append(await _parseElement(doc, subitem, entityStore, data, regs));
                        }
                    }
                    else
                         content.Append(await _parseElement(doc, subitem, entityStore, data, regs));
                }
            }

            result.InnerHtml = content.ToString() + err;

            return result.OuterHtml;
        }

        private async Task<string> _parseTemplate(string template, IEntityStore entityStore, ASObject data, Registers regs, HtmlDocument doc)
        {

            if (!Templates.ContainsKey(template)) throw new InvalidOperationException($"Template {template} does not exist!");
            var templ = Templates[template];
            return await _parseElement(doc, templ, entityStore, data, regs);
        }

        public async Task<string> ParseTemplate(string template, IEntityStore entityStore, APEntity entity, Registers regs = null)
        {
            if (regs == null) regs = new Registers();
            regs.Handler = new ASHandler();
            regs.Renderer = new RendererData();
            var engine = new Engine();
            engine.SetValue("AS", regs.Handler);
            engine.SetValue("Renderer", regs.Renderer);
            engine.SetValue("Data", regs.Data);
            regs.Engine = engine;
            var doc = new HtmlDocument();

            regs.UsedEntities[entity.Id] = entity;

            var data = await _parseTemplate(template, entityStore, entity.Data, regs, doc);
            Console.WriteLine($"{regs.EntityCount} entities gotten in total, {regs.UsedEntities.Count} separate");
            return data;
        }
    }
}
