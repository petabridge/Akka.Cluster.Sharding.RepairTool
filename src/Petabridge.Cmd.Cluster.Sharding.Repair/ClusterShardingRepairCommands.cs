// -----------------------------------------------------------------------
// <copyright file="ClusterShardingRepairCommands.cs" company="Petabridge, LLC">
//      Copyright (C) 2021 - 2021 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Persistence.Query;
using Petabridge.Cmd.Host;
using static Petabridge.Cmd.Cluster.Sharding.Repair.ClusterShardingRepairCmd;

namespace Petabridge.Cmd.Cluster.Sharding.Repair
{
    /// <summary>
    /// Cluster.Sharding repair commands.
    ///
    /// NOTE: this plugin requires you to configure Akka.DependencyInjection to provide support for <see cref="ICurrentPersistenceIdsQuery"/>.
    /// </summary>
    public class ClusterShardingRepairCommands : CommandPaletteHandler
    {
        public static readonly ClusterShardingRepairCommands Instance = new ClusterShardingRepairCommands();
        
        private ClusterShardingRepairCommands() : base(ClusterShardingRepairCommandPalette)
        {
            HandlerProps = Props.Create(() => new ClusterShardingRepairCmdHandler());
        }

        public override Props HandlerProps { get; }

        // public override void OnRegister(PetabridgeCmd plugin)
        // {
        //     // need to validate that end-user configured the plugin correctly
        //     try
        //     {
        //         var dr = DependencyResolver.For(plugin.Sys);
        //         var persistenceIdsQuery = dr.Resolver.GetService<ICurrentPersistenceIdsQuery>();
        //
        //         if (persistenceIdsQuery == default(ICurrentPersistenceIdsQuery))
        //             throw new InvalidOperationException(
        //                 "No ICurrentPersistenceIdsQuery implementation bound to IServiceProvider. Can't start repair.");
        //     }
        //     catch (InvalidOperationException)
        //     {
        //         throw;
        //     }
        //     catch (Exception ex)
        //     {
        //         throw new InvalidOperationException(
        //             "No ICurrentPersistenceIdsQuery implementation bound to IServiceProvider. Can't start repair.", ex);
        //     }
        //     
        //     base.OnRegister(plugin);
        // }
    }
}