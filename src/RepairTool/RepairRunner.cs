﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Petabridge.Cmd.Cluster.Sharding.Repair;

namespace RepairTool
{
    /// <summary>
    /// Implementation of repair tool
    /// </summary>
    public class RepairRunner : IDisposable
    {
        public async Task Start(Func<ActorSystem, ICurrentPersistenceIdsQuery> queryIdMapper, Config config,
            CancellationToken? token = null, bool useConsoleLifetime = true)
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

            var finalToken = token ?? CancellationToken.None;

            if (!config.HasPath("akka.persistence.journal.plugin"))
                throw new ApplicationException(
                    "No akka.persistence.journal.plugin defined inside 'app.conf'. App will not run correctly. " +
                    "Please see https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool for instructions.");

            if (!config.HasPath("akka.persistence.snapshot-store.plugin"))
                throw new ApplicationException(
                    "No akka.persistence.snapshot-store.plugin defined inside 'app.conf'. App will not run correctly. " +
                    "Please see https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool for instructions.");

            var partial = new HostBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddLogging();
                    services.AddSingleton<Config>(config);
                    services.AddSingleton<IPbmClientService, AkkaService>();
                    services.AddTransient<IHostedService, AkkaService>(sp =>
                        (AkkaService) sp.GetRequiredService<IPbmClientService>()); // runs Akka.NET
                    services.AddTransient<ICurrentPersistenceIdsQuery>(sp =>
                    {
                        var pbmService = sp.GetRequiredService<IPbmClientService>();

                        return queryIdMapper(pbmService.Sys);
                    });
                })
                .ConfigureLogging((hostContext, configLogging) => { configLogging.AddConsole(); });

            if (useConsoleLifetime) // toggle this so we can turn it off for unit testing
            {
                partial = partial.UseConsoleLifetime();
            }

            _host = partial.Build();

            await _host.StartAsync(finalToken);
            
            
            ServiceProvider = _host.Services;

            var clientService = _host.Services.GetRequiredService<IPbmClientService>();

            var pbm = clientService.Cmd;
            pbm.RegisterCommandPalette(ClusterShardingRepairCommands.Instance);
            pbm.Start();
            clientService.Sys.Log.Info("Cluster.Sharding.RepairTool ready.");
        }

        public async Task WaitForShutdown(CancellationToken? token = null)
        {
            var finalToken = token ?? CancellationToken.None;
            await _host.WaitForShutdownAsync(finalToken);
        }

        public IServiceProvider ServiceProvider { get; private set; }

        private IHost _host;

        public async ValueTask StopAsync()
        {
            if (_host == null)
            {
                await Task.CompletedTask;
            }
            else
            {
                try
                {
                    // abort after 3 seconds
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _host.StopAsync(cts.Token);
                }
                finally
                {
                    _host.Dispose();
                }
            }
        }

        public void Dispose()
        {
            _host?.Dispose();
        }
    }
}