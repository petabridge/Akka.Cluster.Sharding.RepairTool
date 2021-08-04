using System;
using System.IO;
using System.Threading.Tasks;
using Akka.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RepairTool
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            /*
             * STARTUP CHECK
             *
             * If user has not installed their own Akka.Persistence plugin and provided
             * their own configuration information, display an angry error message and
             * violently crash without doing anything else.
             *
             * This is designed to prevent false starts on the part of the end-user.
             */

            var config = ConfigurationFactory.ParseString(File.ReadAllText("app.conf"));
            if (!config.HasPath("akka.persistence.journal.plugin"))
                throw new ApplicationException(
                    "No akka.persistence.journal.plugin defined inside 'app.conf'. App will not run correctly. " +
                    "Please see https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool for instructions.");
            
            if (!config.HasPath("akka.persistence.snapshot-store.plugin"))
                throw new ApplicationException(
                    "No akka.persistence.snapshot-store.plugin defined inside 'app.conf'. App will not run correctly. " +
                    "Please see https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool for instructions.");
            
            var host = new HostBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddLogging();
                    services.AddSingleton<IPbmClientService, AkkaService>();
                    services.AddHostedService(sp => (IHostedService)sp.GetRequiredService<AkkaService>()); // runs Akka.NET
 
                })
                .ConfigureLogging((hostContext, configLogging) =>
                {
                    configLogging.AddConsole();
                })
                .UseConsoleLifetime()
                .Build();

            await host.RunAsync();
        }
    }
   
}
