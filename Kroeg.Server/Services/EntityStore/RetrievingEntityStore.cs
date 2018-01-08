using System.Net.Http;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Tools;
using Kroeg.Server.Middleware.Renderers;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Kroeg.Server.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kroeg.Server.Services.EntityStore
{
    public class RetrievingEntityStore : IEntityStore
    {
        public IEntityStore Bypass { get; }

        private readonly EntityFlattener _entityFlattener;
        private readonly IServiceProvider _serviceProvider;
        private readonly HttpContext _context;

        public RetrievingEntityStore(IEntityStore next, EntityFlattener entityFlattener, IServiceProvider serviceProvider, IHttpContextAccessor contextAccessor)
        {
            Bypass = next;
            _entityFlattener = entityFlattener;
            _serviceProvider = serviceProvider;
            _context = contextAccessor?.HttpContext;
        }

        private readonly HashSet<string> _collections = new HashSet<string>()
        {
            "https://www.w3.org/ns/activitystreams#OrderedCollection",
            "https://www.w3.org/ns/activitystreams#OrderedCollectionPage",
            "https://www.w3.org/ns/activitystreams#Collection",
            "https://www.w3.org/ns/activitystreams#CollectionPage"
        };

        public async Task<APEntity> GetEntity(string id, bool doRemote)
        {
            if (id == "https://www.w3.org/ns/activitystreams#Public")
            {
                var aso = new ASObject();
                aso.Type.Add("https://www.w3.org/ns/activitystreams#Collection");
                aso.Id = "https://www.w3.org/ns/activitystreams#Public";

                var ent = APEntity.From(aso);
                return ent;
            }
            try {
                if ((new Uri(id)).Host == "localhost") return await Bypass.GetEntity(id, doRemote);
            } catch (UriFormatException) { /* nom */ }

            APEntity entity = null;
            if (Bypass != null) entity = await Bypass.GetEntity(id, doRemote);
            if (entity != null && !entity.IsOwner && entity.Data.Type.Any(_collections.Contains) && doRemote) entity = null;

            var htc = new HttpClient();
            htc.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/activity+json; application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\", application/json, text/html");

            if (_context != null)
            {
                var signatureVerifier = _serviceProvider.GetRequiredService<SignatureVerifier>();
                var user = await Bypass.GetEntity(_context.User.FindFirstValue(JwtTokenSettings.ActorClaim), false);
                if (user != null)
                {
                    var jwt = await signatureVerifier.BuildJWS(user, id);
                    if (jwt != null)
                        htc.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);
                }
            }

            HttpResponseMessage response = null;
            try
            {
                response = await htc.GetAsync(id);
            }
            catch (TaskCanceledException)
            {
                return null; // timeout
            }
            catch (HttpRequestException)
            {
                return null;
            }

            var converters = new List<IConverterFactory> { new AS2ConverterFactory() };
            tryAgain:
            ASObject data = null;
            await response.Content.LoadIntoBufferAsync();
            foreach (var converter in converters)
            {
                if (converter.CanParse && ConverterHelpers.GetBestMatch(converter.MimeTypes, response.Content.Headers.ContentType.ToString()) != null)
                {
                    try {
                        data = await converter.Build(_serviceProvider, null).Parse(await response.Content.ReadAsStreamAsync());
                        break;
                    } catch (NullReferenceException e) { Console.WriteLine(e); /* nom */ }
                }
            }

            if (data == null)
                return null;

            // forces out the old lazy load, if used
            await _entityFlattener.FlattenAndStore(Bypass, data, false);

            return await Bypass.GetEntity(id, true);
        }

        public async Task<APEntity> StoreEntity(APEntity entity) => Bypass == null ? entity : await Bypass.StoreEntity(entity);

        public async Task CommitChanges()
        {
            await Bypass?.CommitChanges();
        }
    }
}
