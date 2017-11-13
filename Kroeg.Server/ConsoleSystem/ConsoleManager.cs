using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Kroeg.ActivityStreams;
using Kroeg.Server.BackgroundTasks;
using Kroeg.Server.Configuration;
using Kroeg.Server.OStatusCompat;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Services.Notifiers;
using Kroeg.Server.Services.Template;
using Kroeg.Server.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Npgsql.Logging;

namespace Kroeg.Server.ConsoleSystem
{
    public class ConsoleManager
    {
        private IServiceCollection _services;
        private IConfigurationRoot _configuration;

        private static Dictionary<string, Type> Commands = new Dictionary<string, Type>()
        {
            ["get"] = typeof(Commands.GetCommand),
            ["attr"] = typeof(Commands.AttributeCommand),
            ["attribute"] = typeof(Commands.AttributeCommand),
            ["t"] = typeof(Commands.TriplesCommand),
            ["triples"] = typeof(Commands.TriplesCommand)
        };

        private class _nullHttpContextAccessor : IHttpContextAccessor
        {
            HttpContext IHttpContextAccessor.HttpContext { get; set; } = null;
        }

        private ConsoleManager()
        {
            _configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();
            
            _services = new ServiceCollection();
            _registerServices();
        }

        public static void Do()
        {
            var manager = new ConsoleManager();
            manager._do();

            while (true)
                Thread.Sleep(-1);
        }

        private async void _do()
        {
            var provider = _services.BuildServiceProvider();
            var sevc = provider.GetService<EntityData>();
            await ActivityStreams.ASObject.SetContext(JsonLDConfig.GetContext(true), sevc.BaseUri + "render/context");


            provider
                .GetService<ILoggerFactory>()
                .AddConsole(LogLevel.Debug);

            while (true) {
                Console.Write("> ");
                var command = Console.ReadLine().Split(' ');

                if (command.Length == 0 || !Commands.ContainsKey(command[0]))
                    continue;
                
                var commandCode = (IConsoleCommand) provider.GetService(Commands[command[0]]);
                try
                {
                    await commandCode.Do(command.Skip(1).ToArray());
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        private void _registerServices()
        {
                        // Add framework services.
            NpgsqlLogManager.IsParameterLoggingEnabled = true;

            _services.AddLogging();
            _services.AddScoped<DbConnection, NpgsqlConnection>((svc) => new NpgsqlConnection(_configuration.GetConnectionString("Default")));
            _services.AddScoped<NpgsqlConnection>((svc) => new NpgsqlConnection(_configuration.GetConnectionString("Default")));
            
            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration.GetSection("Kroeg")["TokenSigningKey"]));
            var tokenSettings = new JwtTokenSettings
            {
                Audience = _configuration.GetSection("Kroeg")["BaseUri"],
                Issuer = _configuration.GetSection("Kroeg")["BaseUri"],
                ExpiryTime = TimeSpan.FromDays(30),
                Credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
                ValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,

                    ValidateIssuer = true,
                    ValidIssuer = _configuration.GetSection("Kroeg")["BaseUri"],

                    ValidateAudience = true,
                    ValidAudience = _configuration.GetSection("Kroeg")["BaseUri"],

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }
            };
            
            _services.AddSingleton(tokenSettings);

            _services.AddSingleton(new EntityData(_configuration.GetSection("Kroeg"))
            {
                EntityNames = _configuration.GetSection("EntityNames")
            });

            _services.AddScoped<INotifier, LocalNotifier>();

            _services.AddSingleton<BackgroundTaskQueuer>();
            _services.AddSingleton(_configuration);

            _services.AddTransient<DeliveryService>();
            _services.AddTransient<RelevantEntitiesService>();
            _services.AddTransient<ActivityService>();
            _services.AddTransient<AtomEntryParser>();
            _services.AddTransient<AtomEntryGenerator>();

            _services.AddScoped<TripleEntityStore>();
            _services.AddScoped<CollectionTools>();
            _services.AddScoped<FakeEntityService>();
            _services.AddScoped<EntityFlattener>();
            _services.AddScoped<KeyService>();

            _services.AddScoped<IEntityStore>((provider) =>
            {
                var triple = provider.GetRequiredService<TripleEntityStore>();
                var flattener = provider.GetRequiredService<EntityFlattener>();
                var httpAccessor = new _nullHttpContextAccessor();
                var fakeEntityService = provider.GetService<FakeEntityService>();
                var retrieving = new RetrievingEntityStore(triple, flattener, provider, httpAccessor);
                return new FakeEntityStore(fakeEntityService, retrieving);
            });
            _services.AddTransient<TemplateService>();
            _services.AddTransient<SignatureVerifier>();


            _services.AddScoped<DatabaseManager>();
            _services.AddTransient<Commands.GetCommand>();
            _services.AddTransient<Commands.AttributeCommand>();
            _services.AddTransient<Commands.TriplesCommand>();
        }
    }
}