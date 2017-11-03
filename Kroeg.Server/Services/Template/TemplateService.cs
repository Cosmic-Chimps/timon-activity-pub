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
        private EntityData _entityData;

        public string PageTemplate { get; private set; }

        public class Registers {
            public Engine Engine { get; set; }
            public ASHandler Handler { get; set; }
            public RendererData Renderer { get; set; }
            public Dictionary<string, APEntity> UsedEntities { get; set; } = new Dictionary<string, APEntity>();
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


            public string sanitize(string data)
            {
                return data.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            }

            private void _clean(HtmlNode node)
            {
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
                        if (!_allowed_classes.IsMatch(cl)) node.RemoveClass(cl);
                    }
                }

                foreach (var child in node.ChildNodes.ToArray()) {
                    _clean(child);
                }
            }

            public string clean(string data)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(data);
                _clean(doc.DocumentNode);
                return doc.DocumentNode.InnerHtml;
            }

            public bool server => true;
            public bool client => false;
        }

        public TemplateService(EntityData entityData)
        {
            _entityData = entityData;
            _parse(_base);
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
            var data = File.ReadAllText(path);
            if (path == "templates/page.html") PageTemplate = data;
            var templateName = path.Substring(_base.Length, path.Length - _base.Length - 5).Replace('\\', '/');
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
            var result = doc.CreateElement(item.Data);
            foreach (var argument in item.Arguments)
            {
                if (!argument.Key.StartsWith("x-"))
                {
                    var resultValue = new StringBuilder();
                    foreach (var subitem in argument.Value)
                        if (subitem.Type == TemplateItemType.Text)
                            resultValue.Append(subitem.Data);
                        else
                            resultValue.Append(_parse(subitem.Data, data, regs));
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
            
            if (item.Arguments.ContainsKey("x-render") && parse)
            {
                var template = item.Arguments["x-render"][0].Data;
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
                else if (objData == null)
                {
                    APEntity obj = await entityStore.GetEntity(id, true);
                    if (obj != null)
                    {
                        regs.UsedEntities[id] = obj;
                        objData = obj.Data;
                    }
                }

                if (objData != null)
                    return await _parseTemplate(template, entityStore, objData, regs, doc);
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
                    APEntity obj = await entityStore.GetEntity(id, true);
                    if (obj != null)
                    {
                        regs.UsedEntities[id] = obj;
                        objData = obj.Data;
                    }
                }

                if (objData != null)
                    return await _parseTemplate(template, entityStore, objData, regs, doc);
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

            result.InnerHtml = content.ToString();

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
            regs.Engine = engine;
            var doc = new HtmlDocument();

            regs.UsedEntities[entity.Id] = entity;

            return await _parseTemplate(template, entityStore, entity.Data, regs, doc);
        }
    }
}
