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
using Kroeg.Server.BackgroundTasks;
using Kroeg.Server.Configuration;
using Kroeg.Server.Middleware;
using Kroeg.Server.Models;
using Kroeg.Server.OStatusCompat;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Kroeg.Server.Services.Notifiers;
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
using Kroeg.Server.Middleware.Handlers;
using System.IO;

namespace Kroeg.Server
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddMvc();

            NpgsqlLogManager.Provider = new ConsoleLoggingProvider(NpgsqlLogLevel.Debug);
            NpgsqlLogManager.IsParameterLoggingEnabled = true;

            services.AddScoped<NpgsqlConnection>((svc) => new NpgsqlConnection(Configuration.GetConnectionString("Default")));
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
                Audience =Configuration.GetSection("Kroeg")["BaseUri"],
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

            services.AddSingleton(new EntityData(Configuration.GetSection("Kroeg"))
            {
                EntityNames = Configuration.GetSection("EntityNames")
            });

            services.AddScoped<INotifier, LocalNotifier>();

            services.AddSingleton<BackgroundTaskQueuer>();
            services.AddSingleton(Configuration);

            services.AddTransient<DeliveryService>();
            services.AddTransient<RelevantEntitiesService>();
            services.AddTransient<ActivityService>();
            services.AddTransient<AtomEntryParser>();
            services.AddTransient<AtomEntryGenerator>();

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
                var retrieving = new RetrievingEntityStore(triple, flattener, provider, httpAccessor);
                return new FakeEntityStore(fakeEntityService, retrieving);
            });
            services.AddSingleton<TemplateService>();
            services.AddTransient<SignatureVerifier>();

            services.AddAuthentication(o => {
                o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                o.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
                .AddJwtBearer((options) => {
                    options.TokenValidationParameters = tokenSettings.ValidationParameters;

                    options.Audience =Configuration.GetSection("Kroeg")["BaseUri"];
                    options.ClaimsIssuer = Configuration.GetSection("Kroeg")["BaseUri"];
                });
            
            services.ConfigureApplicationCookie((options) => {
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

            foreach (var extra in Configuration.GetSection("Kroeg").GetSection("Filters").GetChildren())
            {
                if (typeMap.ContainsKey(extra.Value))
                    EntityData.ExtraFilters.Add(typeMap[extra.Value]);
            }

            services.AddScoped<DatabaseManager>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public async void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug(LogLevel.Trace);

            app.UseAuthentication();
            app.UseWebSockets();
            app.UseStaticFiles();

            app.UseDeveloperExceptionPage();
            app.UseMiddleware<WebSubMiddleware>();
            app.UseMiddleware<GetEntityMiddleware>();
            app.UseMvc();

            app.ApplicationServices.GetRequiredService<DatabaseManager>().EnsureExists();
            app.ApplicationServices.GetRequiredService<BackgroundTaskQueuer>(); // kickstart background tasks!

            var sevc = app.ApplicationServices.GetRequiredService<EntityData>();
            await ActivityStreams.ASObject.SetContext(JsonLDConfig.GetContext(true), sevc.BaseUri + "render/context");
        }
    }
}
