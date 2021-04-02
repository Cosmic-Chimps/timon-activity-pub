using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Kroeg.Server.Configuration;
using Kroeg.Server.Middleware;
using Kroeg.EntityStore.Models;
using Kroeg.EntityStore.Store;
using Microsoft.AspNetCore.Http;
using Kroeg.Server.Services.Template;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using System.Data;
using System.Data.Common;
using Npgsql.Logging;
using System.Runtime.Loader;
using System.Collections.Generic;
using Kroeg.Services;
using System.IO;
using Kroeg.ActivityPub.Shared;
using Kroeg.ActivityPub.ServerToServer;
using Kroeg.ActivityPub.ClientToServer;
using Kroeg.ActivityPub;
using Kroeg.EntityStore;
using Kroeg.EntityStore.Services;
using Kroeg.EntityStore.Notifier;
using Kroeg.EntityStore.Salmon;
using Kroeg.ActivityPub.Services;
using Microsoft.Extensions.Hosting;

namespace Kroeg.Server
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        readonly IServerConfig _serverConfig;

        public Startup(IConfiguration configuration, IServerConfig serverConfig)
        {
            Configuration = configuration;
            _serverConfig = serverConfig;
        }



        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddMvc();

            var connectionString = Configuration.GetValue<string>("CONNECTION_STRING") ?? Configuration.GetConnectionString("Default");

            services.AddScoped<NpgsqlConnection>((svc) => new NpgsqlConnection(connectionString));
            services.AddScoped<DbConnection, NpgsqlConnection>((svc) => svc.GetService<NpgsqlConnection>());

            services.AddTransient<IUserStore<APUser>, KroegUserStore>();
            services.AddTransient<IUserPasswordStore<APUser>, KroegUserStore>();
            services.AddTransient<IRoleStore<IdentityRole>, KroegUserStore>();

            services.AddIdentity<APUser, IdentityRole>()
                .AddDefaultTokenProviders();

            services.AddAuthorization(options =>
            {
                options.AddPolicy("admin", policy => policy.RequireClaim("admin"));
                options.AddPolicy("pass", policy => policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser());
            });

            services.Configure<IdentityOptions>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 0;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
            });

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration.GetSection("Kroeg")["TokenSigningKey"]));
            var tokenSettings = new JwtTokenSettings
            {
                Audience = Configuration.GetSection("Kroeg")["BaseUri"],
                Issuer = Configuration.GetSection("Kroeg")["BaseUri"],
                ExpiryTime = TimeSpan.FromDays(30),
                Credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
                ValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,

                    ValidateIssuer = true,
                    ValidIssuer = Configuration.GetSection("Kroeg")["BaseUri"],

                    ValidateAudience = true,
                    ValidAudience = Configuration.GetSection("Kroeg")["BaseUri"],

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }
            };
            services.AddSingleton(tokenSettings);

            services.AddSingleton<IServerConfig>(_serverConfig);
            services.AddSingleton<IConfiguration>(Configuration);

            services.AddSingleton<URLService>();

            services.AddScoped<INotifier, LocalNotifier>();

            services.AddSingleton(Configuration);
            services.AddTransient<IAuthorizer, DefaultAuthorizer>();

            services.AddTransient<DeliveryService>();
            services.AddTransient<RelevantEntitiesService>();

            services.AddScoped<TripleEntityStore>();
            services.AddScoped<CollectionTools>();
            services.AddScoped<FakeEntityService>();
            services.AddScoped<EntityFlattener>();
            services.AddScoped<KeyService>();

            services.AddScoped<IEntityStore>((provider) =>
            {
                var triple = provider.GetRequiredService<TripleEntityStore>();
                var flattener = provider.GetRequiredService<EntityFlattener>();
                var httpAccessor = provider.GetService<IHttpContextAccessor>();
                var fakeEntityService = provider.GetService<FakeEntityService>();
                var keyService = provider.GetService<KeyService>();
                var retrieving = new RetrievingEntityStore(triple, flattener, provider, keyService, httpAccessor);
                return new FakeEntityStore(fakeEntityService, retrieving);
            });
            services.AddSingleton<TemplateService>();
            services.AddTransient<SignatureVerifier>();

            services.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                o.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
                .AddJwtBearer("Bearer", (options) =>
                 {
                     options.TokenValidationParameters = tokenSettings.ValidationParameters;

                     options.Audience = Configuration.GetSection("Kroeg")["BaseUri"];
                     options.ClaimsIssuer = Configuration.GetSection("Kroeg")["BaseUri"];
                 });

            services.ConfigureApplicationCookie((options) =>
            {
                options.Cookie.Name = "Kroeg.Auth";
                options.LoginPath = "/auth/login";
            });

            var typeMap = new Dictionary<string, Type>();

            foreach (var module in Configuration.GetSection("Kroeg").GetSection("Modules").GetChildren())
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Directory.GetCurrentDirectory(), module.Value));
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsSubclassOf(typeof(BaseHandler)))
                        typeMap[type.FullName] = type;
                }
            }

            ServerConfig.ClientToServerHandlers.AddRange(new Type[] {
                typeof(ObjectWrapperHandler),
                typeof(ActivityMissingFieldsHandler),
                typeof(CreateActivityHandler),
                // commit changes before modifying collections
                typeof(UpdateDeleteActivityHandler),
                typeof(CommitChangesHandler),
                typeof(AcceptRejectFollowHandler),
                typeof(FollowLikeHandler),
                typeof(AddRemoveActivityHandler),
                typeof(UndoActivityHandler),
                typeof(BlockHandler),
                typeof(CreateActorHandler),
                typeof(DeliveryHandler)
            });

            ServerConfig.ServerToServerHandlers.AddRange(new Type[] {
                typeof(VerifyOwnershipHandler),
                typeof(DeleteHandler),
                typeof(FollowResponseHandler),
                typeof(LikeFollowAnnounceHandler),
                typeof(AddRemoveActivityHandler),
                typeof(UndoHandler),
                typeof(CreateHandler),
                typeof(DeliveryHandler)
            });

            ServerConfig.Converters.AddRange(new IConverterFactory[]
            {
                new AS2ConverterFactory()
            });

            foreach (var extra in Configuration.GetSection("Kroeg").GetSection("Filters").GetChildren())
            {
                if (typeMap.ContainsKey(extra.Value))
                {
                    ServerConfig.ClientToServerHandlers.Add(typeMap[extra.Value]);
                    ServerConfig.ServerToServerHandlers.Add(typeMap[extra.Value]);
                }
            }

            services.AddScoped<DatabaseManager>();

            services.AddControllers().AddDapr();

            services.AddHealthChecks();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public async void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            app.UseAuthentication();

            app.UseWebSockets();
            app.UseStaticFiles();

            app.UseMiddleware<GetEntityMiddleware>();

            app.UseRouting();

            app.UseAuthorization();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCloudEvents();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapSubscribeHandler();
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/health");
            });

            app.ApplicationServices.GetRequiredService<DatabaseManager>().EnsureExists();

            for (int i = 0; i < int.Parse(Configuration.GetSection("Kroeg")["BackgroundThreads"]); i++)
            {
                var serviceProvider = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>().CreateScope().ServiceProvider;
                ActivatorUtilities.CreateInstance<BackgroundTaskQueuer>(serviceProvider);
            }

            var sevc = app.ApplicationServices.GetRequiredService<IServerConfig>();
            await ActivityStreams.ASObject.SetContext(JsonLDConfig.GetContext(true), sevc.BaseUri + "render/context");
        }
    }
}
