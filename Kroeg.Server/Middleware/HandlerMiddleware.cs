using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kroeg.ActivityStreams;
using Kroeg.Server.Configuration;
using Kroeg.Server.Middleware.Handlers;
using Kroeg.Server.Middleware.Handlers.ClientToServer;
using Kroeg.Server.Middleware.Handlers.ServerToServer;
using Kroeg.Server.Middleware.Handlers.Shared;
using Kroeg.Server.Models;
using Kroeg.Server.OStatusCompat;
using Kroeg.Server.Salmon;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Kroeg.Server.Middleware.Renderers;
using Kroeg.Server.BackgroundTasks;
using System.Data;
using Dapper;
using System.Data.Common;
using SharpRaven;
using SharpRaven.Data;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Middleware
{
    public class HandlerMiddleware
    {
        private readonly RequestDelegate _next;

        public HandlerMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, RavenClient raven)
        {
            try
            {
                await _next(context);
            }
            catch (Exception e)
            {
                var fullpath = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}";
                var revent = new SentryEvent(e);
                revent.Tags.Add("Url", fullpath);
                await raven.CaptureAsync(revent);
                throw;
            }
        }
    }
}
