// -----------------------------------------------------------------------
// <copyright file="ClusterShardingRepairCommands.cs" company="Petabridge, LLC">
//      Copyright (C) 2021 - 2021 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Petabridge.Cmd.Host;
using static Petabridge.Cmd.Cluster.Sharding.Repair.ClusterShardingRepairCmd;

namespace Petabridge.Cmd.Cluster.Sharding.Repair
{
    public class ClusterShardingRepairCommands : CommandPaletteHandler
    {
        public static ClusterShardingRepairCommands Instance = new ClusterShardingRepairCommands();
        
        private ClusterShardingRepairCommands() : base(ClusterShardingRepairCommandPalette)
        {
            HandlerProps = Props.Create(() => new ClusterShardingRepairCmdHandler());
        }

        public override Props HandlerProps { get; }

    }
}