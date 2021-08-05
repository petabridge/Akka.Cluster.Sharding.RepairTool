// -----------------------------------------------------------------------
// <copyright file="ClusterShardingRepairCmdRouter.cs" company="Petabridge, LLC">
//      Copyright (C) 2021 - 2021 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.DependencyInjection;
using Petabridge.Cmd.Host;
using static Petabridge.Cmd.Cluster.Sharding.Repair.ClusterShardingRepairCmd;

namespace Petabridge.Cmd.Cluster.Sharding.Repair
{
    /// <summary>
    ///     INTERNAL API.
    ///     Used to execute <see cref="ClusterShardingRepairCmd.ClusterShardingRepairCommandPalette" />
    /// </summary>
    internal sealed class ClusterShardingRepairCmdHandler: CommandHandlerActor
    {
        private int _printCounter = 0;
        
        public ClusterShardingRepairCmdHandler() : base(ClusterShardingRepairCommandPalette)
        {
            Process(PrintInternalClusterShardingData.Name, cmd =>
            {
                var sp = DependencyResolver.For(Context.System);
                var sender = Sender;
                var props = sp.Props<ClusterShardingEntityPrinter>(sender, false);
                Context.ActorOf(props, "printer" + _printCounter++);
            });
            
            Process(PrintShardRegionNameData.Name, cmd =>
            {
                var sp = DependencyResolver.For(Context.System);
                var sender = Sender;
                var props = sp.Props<ClusterShardingEntityPrinter>(sender, true);
                Context.ActorOf(props, "printer" + _printCounter++);
            });
            
            Process(RemoveInternalClusterShardingData.Name, command =>
            {
                var journalPluginId = command.Arguments
                    .SingleOrDefault(x =>
                        RemoveInternalClusterShardingData.ArgumentsByName["journalPluginId"].Switch.Contains(x.Item1))?.Item2;

                if (string.IsNullOrWhiteSpace(journalPluginId))
                    journalPluginId = Context.System.Settings.Config.GetString("akka.cluster.sharding.journal-plugin-id");
                
                if (string.IsNullOrWhiteSpace(journalPluginId))
                    journalPluginId = Context.System.Settings.Config.GetString("akka.persistence.journal.plugin");
                
                var snapshotPluginId = command.Arguments
                    .SingleOrDefault(x => 
                        RemoveInternalClusterShardingData.ArgumentsByName["snapshotPluginId"].Switch.Contains(x.Item1))?.Item2;
                
                if (string.IsNullOrWhiteSpace(snapshotPluginId))
                    snapshotPluginId = Context.System.Settings.Config.GetString("akka.cluster.sharding.snapshot-plugin-id");
                
                if (string.IsNullOrWhiteSpace(snapshotPluginId))
                    snapshotPluginId = Context.System.Settings.Config.GetString("akka.persistence.snapshot-store.plugin");
                
                var typeNames = new HashSet<string>(command.Arguments
                    .Where(x =>
                        RemoveInternalClusterShardingData.ArgumentsByName["typeName"].Switch.Contains(x.Item1))
                    .Select(x => x.Item2));
                
                Context.ActorOf(
                    ClusterShardingRepairCommandProcessor.Props(journalPluginId, snapshotPluginId, typeNames, Sender), 
                    nameof(ClusterShardingRepairCommandProcessor));
            });
        }
    }
}