using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Kroeg.Server.Services.Template
{
    public enum TemplateItemType
    {
        Element,
        Text,
        Script
    }

    public struct TemplateItem
    {
        public TemplateItemType Type { get; set; }
        public string Data { get; set; }
        public List<TemplateItem> Children { get; set; }
        public Dictionary<string, List<TemplateItem>> Arguments { get; set; }
    }

    public class TemplateParser
    {
        private static List<TemplateItem> _parseStringOrScript(string data, bool alwaysScript)
        {
            var start = 0;
            var items = new List<TemplateItem>();
            while (start < data.Length)
            {
                var next = data.IndexOf("{{", start);
                if (next == -1)
                {
                    items.Add(new TemplateItem { Type = alwaysScript ? TemplateItemType.Script : TemplateItemType.Text, Data = data.Substring(start)});
                    break;
                }

                var after = data.IndexOf("}}", next);
                
                if (next != start)
                    items.Add(new TemplateItem { Type = alwaysScript ? TemplateItemType.Script : TemplateItemType.Text, Data = data.Substring(start, next - start - 1) });
                items.Add(new TemplateItem { Type = TemplateItemType.Script, Data = data.Substring(next + 2, after - next - 2)});

                start = after + 2;
            }

            return items;
        }

        private static HashSet<string> _items = new HashSet<string>() { "x-if", "x-render-id", "x-else", "x-for-in" };

        private static TemplateItem _parseElement(HtmlNode element)
        {
            var item = new TemplateItem {
                Type = TemplateItemType.Element,
                Data = element.OriginalName,
                Arguments = element.Attributes.ToDictionary(a => a.Name, a => _parseStringOrScript(a.Value, _items.Contains(a.Name))),
                Children = new List<TemplateItem>()
            };
            
            foreach (var child in element.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Text)
                    item.Children.AddRange(_parseStringOrScript(child.InnerHtml, false));
                else if (child.NodeType == HtmlNodeType.Element)
                    item.Children.Add(_parseElement(child));
            }

            return item;
        }

        public static TemplateItem Parse(string template)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(template);
            var elem = _parseElement(doc.DocumentNode.ChildNodes[0]);
            return elem;
        }
    }
}
