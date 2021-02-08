using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using Microsoft.Extensions.Logging;
using System.Linq;
using Kroeg.EntityStore.Services;

namespace Kroeg.Server
{
  public class Program
  {
    public static void Main(string[] args)
    {
      if (args.Contains("console"))
      {
        ConsoleSystem.ConsoleManager.Do();
        return;
      }

      var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

      var config = new ConfigurationBuilder()
          .SetBasePath(Directory.GetCurrentDirectory())
          .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
          .AddJsonFile($"appsettings.{environment}.json", optional: true)
          .AddEnvironmentVariables()
          .Build();

      var listenOn = new Uri(args.Length == 0 ? "http://0.0.0.0:5000/" : args[0]);
      var builder = new UriBuilder(listenOn);

      var serverConfig = new ServerConfig(config.GetSection("Kroeg"));
      builder.Path = serverConfig.BasePath;

      var host = new WebHostBuilder()
          .UseKestrel()
          .UseContentRoot(Directory.GetCurrentDirectory())
          .UseIISIntegration()
          .UseStartup<Startup>(ctx =>
          {
            return new Startup(config, serverConfig);
          })
          .UseUrls(new string[] { builder.Uri.ToString() })
          .ConfigureLogging((hostingContext, logging) =>
          {
            logging.AddConsole();

          })
          .Build();

      host.Run();
    }
  }
}
