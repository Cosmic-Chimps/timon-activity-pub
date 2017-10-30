﻿using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using System.IO;
using Kroeg.Server.Models;
using Kroeg.Server.OStatusCompat;
using System.Xml.Linq;

namespace Kroeg.Server.Middleware.Renderers
{
    public class AtomConverterFactory : IConverterFactory
    {
        public bool CanParse => true;
        public string FileExtension => "atom";
        public bool CanRender => true;

        private bool _isPost;

        public List<string> MimeTypes => new List<string> { "application/atom+xml" };

        public string RenderMimeType => MimeTypes[0];

        public AtomConverterFactory(bool isPost)
        {
            _isPost = isPost;
        }

        public IConverter Build(IServiceProvider serviceProvider, string target)
        {
            var converter = ActivatorUtilities.CreateInstance<AtomConverter>(serviceProvider, this);
            converter._targetUser = target;
            return converter;
        }

        private class AtomConverter : IConverter
        {
            private IEntityStore _entityStore;
            private EntityFlattener _flattener;
            private AtomConverterFactory _factory;

            private AtomEntryParser _entryParser;
            private AtomEntryGenerator _entryGenerator;
            internal string _targetUser;

            public AtomConverter(IEntityStore entityStore, EntityFlattener flattener, AtomEntryParser parser, AtomEntryGenerator generator, AtomConverterFactory factory)
            {
                _entityStore = entityStore;
                _flattener = flattener;
                _factory = factory;

                _entryParser = parser;
                _entryGenerator = generator;
            }

            public async Task<ASObject> Parse(Stream request)
            {
                string data;
                using (var r = new StreamReader(request))
                    data = await r.ReadToEndAsync();

                return await _entryParser.Parse(XDocument.Parse(data), _factory._isPost, _targetUser);
            }

            public async Task Render(HttpRequest request, HttpResponse response, APEntity toRender)
            {
                response.ContentType = ConverterHelpers.GetBestMatch(_factory.MimeTypes, request.Headers["Accept"]);

                var doc = await _entryGenerator.Build(toRender.Data);
                await response.WriteAsync(doc.ToString());
            }
        }
    }
}
