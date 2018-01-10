using System.Net.Http;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.EntityStore.Models;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Kroeg.EntityStore.Services;
using Kroeg.EntityStore.Salmon;
using Kroeg.Services;

namespace Kroeg.EntityStore.Store
{
    public class RetrievingEntityStore : IEntityStore
    {
        public IEntityStore Bypass { get; }

        private readonly EntityFlattener _entityFlattener;
        private readonly IServiceProvider _serviceProvider;
        private readonly HttpContext _context;
        private readonly KeyService _keyService;
    	private RelevantEntitiesService _relevantEntities;

        public RetrievingEntityStore(IEntityStore next, EntityFlattener entityFlattener, IServiceProvider serviceProvider, KeyService keyService, IHttpContextAccessor contextAccessor)
        {
            Bypass = next;
            _entityFlattener = entityFlattener;
            _serviceProvider = serviceProvider;
            _keyService = keyService;
            _context = contextAccessor?.HttpContext;
        }

	private RelevantEntitiesService _getRE() => _relevantEntities = _relevantEntities ?? _serviceProvider.GetService<RelevantEntitiesService>();

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
            string origin = id;
            try {
                var uri = new Uri(id);
                if (uri.Host == "localhost") return await Bypass.GetEntity(id, doRemote);
                origin = uri.GetLeftPart(UriPartial.Authority);
            } catch (UriFormatException) { /* nom */ }

            APEntity entity = null;
            if (Bypass != null) entity = await Bypass.GetEntity(id, doRemote);
/*            if (entity == null)
            {
                var possibilities = (await _getRE().Query(new RelevantEntitiesService.ContainsAnyStatement("https://www.w3.org/ns/activitystreams#url") { id })).Where(a => Uri.IsWellFormedUriString(a.Id, UriKind.Absolute) && (new Uri(a.Id)).GetLeftPart(UriPartial.Authority) == origin).ToList();
                if (possibilities.Count == 1) entity = possibilities.First();
            }*/
            if (entity != null && !entity.IsOwner && entity.Data.Type.Any(_collections.Contains) && doRemote) entity = null;
            if (entity != null || !doRemote) return entity;

            var htc = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, id);
            request.Headers.TryAddWithoutValidation("Accept", "application/activity+json; application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\", application/json, text/html");

            if (_context != null)
            {
                var signatureVerifier = _serviceProvider.GetRequiredService<SignatureVerifier>();
                var user = await Bypass.GetEntity(_context.User.FindFirstValue("actor"), false);
                if (user != null)
                {
                    var jwt = await signatureVerifier.BuildJWS(user, id);
                    if (jwt != null)
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);
                    request.Headers.TryAddWithoutValidation("Signature", await _keyService.BuildHTTPSignature(user.Id, request));
                }
            }

            HttpResponseMessage response = null;
            try
            {
                response = await htc.SendAsync(request);
            }
            catch (TaskCanceledException)
            {
                return null; // timeout
            }
            catch (HttpRequestException)
            {
                return null;
            }

            ASObject data = null;
            await response.Content.LoadIntoBufferAsync();
            foreach (var converter in ServerConfig.Converters)
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
