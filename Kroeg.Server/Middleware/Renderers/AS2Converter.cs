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
using Newtonsoft.Json;

namespace Kroeg.Server.Middleware.Renderers
{
    public class AS2ConverterFactory : IConverterFactory
    {
        public bool CanParse => true;
        public string FileExtension => "json";
        public bool CanRender => true;

        public List<string> MimeTypes => new List<string> { "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"", "application/activity+json", "application/json" };

        public string RenderMimeType => MimeTypes[0];

        public IConverter Build(IServiceProvider serviceProvider, string target)
        {
            return ActivatorUtilities.CreateInstance<AS2Converter>(serviceProvider, this);
        }

        private class AS2Converter : IConverter
        {
            private IEntityStore _entityStore;
            private EntityFlattener _flattener;
            private AS2ConverterFactory _factory;

            public AS2Converter(IEntityStore entityStore, EntityFlattener flattener, AS2ConverterFactory factory)
            {
                _entityStore = entityStore;
                _flattener = flattener;
                _factory = factory;
            }

            public async Task<ASObject> Parse(Stream request)
            {
                string data;
                using (var r = new StreamReader(request))
                    data = await r.ReadToEndAsync();

                return ASObject.Parse(data, true);
            }

            public async Task Render(HttpRequest request, HttpResponse response, APEntity toRender)
            {
                response.ContentType = ConverterHelpers.GetBestMatch(_factory.MimeTypes, request.Headers["Accept"]);
                if (toRender.Type.Contains("Tombstone"))
                    response.StatusCode = 410;

                if (request.Method == "POST")
                {
                    response.StatusCode = 201;
                    response.Headers.Add("Location", toRender.Id);
                }

                response.Headers.Add("Access-Control-Allow-Origin", "*");

                var depth = Math.Min(int.Parse(request.Query["depth"].FirstOrDefault() ?? "3"), 5);
                var unflattened = await _flattener.Unflatten(_entityStore, toRender, depth, isOwner: toRender.IsOwner);

                await response.WriteAsync(unflattened.Serialize(true).ToString());
            }
        }
    }
}
