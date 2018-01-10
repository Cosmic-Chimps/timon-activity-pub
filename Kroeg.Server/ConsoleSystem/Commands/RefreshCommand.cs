using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dapper;
using Kroeg.ActivityPub;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Services;
using Kroeg.EntityStore.Store;

namespace Kroeg.Server.ConsoleSystem.Commands
{
    public class RefreshCommand : IConsoleCommand
    {
        private readonly IEntityStore _entityStore;
        private readonly EntityFlattener _entityFlattener;
        private readonly IServiceProvider _provider;

        public RefreshCommand(IEntityStore entityStore, EntityFlattener entityFlattener, IServiceProvider provider)
        {
            _entityStore = entityStore;
            _entityFlattener = entityFlattener;
            _provider = provider;
        }

        public async Task Do(string[] args)
        {
            foreach (var loadUrl in args)
            {
                var htc = new HttpClient();
                htc.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\", application/activity+json, application/json, text/html");

                var response = await htc.GetAsync(loadUrl);

                var converter = new AS2ConverterFactory();

                var data = await converter.Build(_provider, null).Parse(await response.Content.ReadAsStreamAsync());

                var result = await _entityFlattener.FlattenAndStore(_entityStore, data, false);

                Console.WriteLine($"{result.Id} stored");
            }
        }
    }
}