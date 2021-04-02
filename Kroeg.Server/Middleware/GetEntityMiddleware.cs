using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.EntityFrameworkCore.Storage;
using Kroeg.ActivityPub;
using Kroeg.EntityStore.Services;
using System.IO;
using System.Text;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Middleware
{
    public class GetEntityMiddleware
    {
        private readonly RequestDelegate _next;
        private List<IConverterFactory> _converters;

        public GetEntityMiddleware(RequestDelegate next)
        {
            _next = next;
            _converters = new List<IConverterFactory>
            {
                new AS2ConverterFactory()
            };
        }

        public async Task Invoke(HttpContext context, IServiceProvider serviceProvider, IServerConfig serverConfig, IEntityStore store)
        {
            var handler = ActivatorUtilities.CreateInstance<GetEntityHandler>(serviceProvider, context.User);
            if (serverConfig.RewriteRequestScheme) context.Request.Scheme = "https";

            // var fullpath = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}";
            var fullpath = $"{serverConfig.BaseUriWithoutTrailing}{context.Request.Path}";
            foreach (var converterFactory in _converters)
            {
                if (converterFactory.FileExtension != null && fullpath.EndsWith("." + converterFactory.FileExtension))
                {
                    fullpath = fullpath.Substring(0, fullpath.Length - 1 - converterFactory.FileExtension.Length);
                    context.Request.Headers.Remove("Accept");
                    context.Request.Headers.Add("Accept", converterFactory.RenderMimeType);
                    break;
                }
            }

            if (!context.Request.Path.ToUriComponent().StartsWith("/auth"))
            {
                context.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
                context.Response.Headers.Add("Access-Control-Allow-Headers", "Authorization");
                context.Response.Headers.Add("Access-Control-Expose-Headers", "Link");
                context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST");
                context.Response.Headers.Add("Access-Control-Allow-Origin", context.Request.Headers["Origin"]);
                context.Response.Headers.Add("Vary", "Origin");
            }

            /* && ConverterHelpers.GetBestMatch(_converters[0].MimeTypes, context.Request.Headers["Accept"]) != null */
            if (context.Request.Method == "OPTIONS")
            {
                context.Response.StatusCode = 200;
                return;
            }

            Console.WriteLine(fullpath);
            foreach (var line in context.Request.Headers["Accept"]) Console.WriteLine($"---- {line}");

            if (context.Request.Headers["Accept"].Contains("text/event-stream"))
            {
                await handler.EventStream(context, fullpath);
                return;
            }


            if (context.WebSockets.IsWebSocketRequest)
            {
                await handler.WebSocket(context, fullpath);
                return;
            }

            if (context.Request.Method == "POST" && context.Request.ContentType.StartsWith("multipart/form-data"))
            {
                context.Items.Add("fullPath", fullpath);
                context.Request.Path = "/settings/uploadMedia";
                await _next(context);
                return;
            }

            IConverter readConverter = null;
            IConverter writeConverter = null;
            bool needRead = context.Request.Method == "POST";
            var target = fullpath.Replace("127.0.0.1", "localhost");
            APEntity targetEntity = null;
            targetEntity = await store.GetEntity(target, false);

            if (needRead)
            {
                if (targetEntity?.Type == "_inbox")
                    target = targetEntity.Data["attributedTo"].Single().Id;
            }

            if (targetEntity == null)
            {
                // using var ms = new MemoryStream();
                // await context.Request.Body.CopyToAsync(ms);
                // var x = Encoding.UTF8.GetString(ms.ToArray());
                // System.Console.WriteLine(x);
                await _next(context);
                return;
            }


            var acceptHeaders = context.Request.Headers["Accept"];
            if (acceptHeaders.Count == 0 && context.Request.ContentType != null)
            {
                acceptHeaders.Append(context.Request.ContentType);
            }

            foreach (var converterFactory in _converters)
            {
                bool worksForWrite = converterFactory.CanRender && ConverterHelpers.GetBestMatch(converterFactory.MimeTypes, acceptHeaders) != null;
                bool worksForRead = needRead && converterFactory.CanParse && ConverterHelpers.GetBestMatch(converterFactory.MimeTypes, context.Request.ContentType) != null;

                if (worksForRead && worksForWrite && readConverter == null && writeConverter == null)
                {
                    readConverter = writeConverter = converterFactory.Build(serviceProvider, target);
                    break;
                }

                if (worksForRead && readConverter == null)
                    readConverter = converterFactory.Build(serviceProvider, target);

                if (worksForWrite && writeConverter == null)
                    writeConverter = converterFactory.Build(serviceProvider, target);
            }

            ASObject data = null;
            if (readConverter != null)
                data = await readConverter.Parse(context.Request.Body);

            if (needRead && readConverter != null && writeConverter == null) writeConverter = readConverter;

            if (data == null && needRead && targetEntity != null)
            {
                context.Response.StatusCode = 415;
                await context.Response.WriteAsync("Unknown mime type " + context.Request.ContentType);
                return;
            }

            var arguments = context.Request.Query;
            APEntity entOut = null;

            try
            {
                if (context.Request.Method == "GET" || context.Request.Method == "HEAD" || context.Request.Method == "OPTIONS")
                {
                    entOut = await handler.Get(fullpath, arguments, context, targetEntity);
                    if (entOut?.Id == "kroeg:unauthorized" && writeConverter != null) throw new UnauthorizedAccessException("no access");
                }
                else if (context.Request.Method == "POST" && data != null)
                {
                    await handler._connection.OpenAsync();
                    using (var transaction = handler._connection.BeginTransaction())
                    {
                        entOut = await handler.Post(context, fullpath, targetEntity, data);
                        transaction.Commit();
                    }
                }
            }
            catch (UnauthorizedAccessException e)
            {
                context.Response.StatusCode = 403;
                Console.WriteLine(e);
                await context.Response.WriteAsync(e.Message);
            }
            catch (InvalidOperationException e)
            {
                context.Response.StatusCode = 401;
                Console.WriteLine(e);
                await context.Response.WriteAsync(e.Message);
            }

            if (context.Response.HasStarted)
                return;

            if (entOut != null)
            {

                if (context.Request.Method == "HEAD")
                {
                    context.Response.StatusCode = 200;
                    return;
                }

                if (writeConverter != null)
                    await writeConverter.Render(context.Request, context.Response, entOut);
                else if (context.Request.ContentType == "application/magic-envelope+xml")
                {
                    context.Response.StatusCode = 202;
                    await context.Response.WriteAsync("accepted");
                }
                else
                {
                    context.Request.Method = "GET";
                    context.Request.Path = "/render";
                    context.Items["object"] = entOut;
                    await _next(context);
                }
                return;
            }

            if (!context.Response.HasStarted)
            {
                await _next(context);
            }
        }
    }
}
