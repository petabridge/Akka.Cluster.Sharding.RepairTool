using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Bootstrap.Docker;
using Akka.Configuration;
using Akka.DependencyInjection;
using Akka.Persistence;
using Microsoft.Extensions.Hosting;
using Petabridge.Cmd.Cluster.Sharding.Repair;
using Petabridge.Cmd.Host;

namespace RepairTool
{
    internal sealed class PlumbBob : ReceivePersistentActor
    {
        public override string PersistenceId => "plumbbob";

        public PlumbBob()
        {
            CommandAny(_ => {});
        }
    } 
    
    /// <summary>
    /// <see cref="IHostedService"/> that runs and manages <see cref="ActorSystem"/> in background of application.
    /// </summary>
    public class AkkaService : IHostedService, IPbmClientService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Config _config;

        public AkkaService(IServiceProvider serviceProvider, Config config)
        {
            _serviceProvider = serviceProvider;
            _config = config;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var bootstrap = BootstrapSetup.Create()
                .WithConfig(_config) // load HOCON
                .WithActorRefProvider(ProviderSelection.Cluster.Instance); // launch Akka.Cluster

            // N.B. `WithActorRefProvider` isn't actually needed here - the HOCON file already specifies Akka.Cluster

            // enable DI support inside this ActorSystem, if needed
            var diSetup = DependencyResolverSetup.Create(_serviceProvider);

            // merge this setup (and any others) together into ActorSystemSetup
            var actorSystemSetup = bootstrap.And(diSetup);

            // start ActorSystem
            Sys = ActorSystem.Create("ClusterSys", actorSystemSetup);

            // loads the default Akka.Persistence plugin settings behind the scenes
            Sys.ActorOf(Props.Create(() => new PlumbBob()), "plumbbob");

            // start Petabridge.Cmd (https://cmd.petabridge.com/)
            var pbm = PetabridgeCmd.Get(Sys);

            // expose to external services
            Cmd = pbm;

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
