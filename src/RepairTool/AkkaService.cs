using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Bootstrap.Docker;
using Akka.Configuration;
using Akka.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Petabridge.Cmd.Cluster.Sharding.Repair;
using Petabridge.Cmd.Host;

namespace RepairTool
{
    /// <summary>
    /// <see cref="IHostedService"/> that runs and manages <see cref="ActorSystem"/> in background of application.
    /// </summary>
    public class AkkaService : IHostedService, IPbmClientService
    {
        private readonly IServiceProvider _serviceProvider;

        public AkkaService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
             var config = ConfigurationFactory.ParseString(File.ReadAllText("app.conf")).BootstrapFromDocker();
             var bootstrap = BootstrapSetup.Create()
                .WithConfig(config) // load HOCON
                .WithActorRefProvider(ProviderSelection.Cluster.Instance); // launch Akka.Cluster

            // N.B. `WithActorRefProvider` isn't actually needed here - the HOCON file already specifies Akka.Cluster

            // enable DI support inside this ActorSystem, if needed
            var diSetup = ServiceProviderSetup.Create(_serviceProvider);

            // merge this setup (and any others) together into ActorSystemSetup
            var actorSystemSetup = bootstrap.And(diSetup);

            // start ActorSystem
            Sys = ActorSystem.Create("ClusterSys", actorSystemSetup);

            // start Petabridge.Cmd (https://cmd.petabridge.com/)
            var pbm = PetabridgeCmd.Get(Sys);
            pbm.RegisterCommandPalette(ClusterShardingRepairCommands.Instance);
            pbm.Start(); // begin listening for PBM management commands

            // expose to external services
            Cmd = pbm;

            // instantiate actors

            // use the ServiceProvider ActorSystem Extension to start DI'd actors
            var sp = ServiceProvider.For(Sys);
            
            Sys.Log.Info("Akka.Cluster.Sharding.RepairTool started. Connect with a Petabridge.Cmd (https://cmd.petabridge.com/) client to get started.");
            Sys.Log.Warning("This application should never be run when connected to a live, running cluster. Always make sure sharding is not in-use before using this.");
            
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            // strictly speaking this may not be necessary - terminating the ActorSystem would also work
            // but this call guarantees that the shutdown of the cluster is graceful regardless
             await CoordinatedShutdown.Get(Sys).Run(CoordinatedShutdown.ClrExitReason.Instance);
        }

        public PetabridgeCmd Cmd { get; private set; }
        public ActorSystem Sys { get; private set; }
    }
   
}
