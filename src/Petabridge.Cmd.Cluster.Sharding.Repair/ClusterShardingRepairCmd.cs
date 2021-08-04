// -----------------------------------------------------------------------
// <copyright file="ClusterShardingRepairCmd.cs" company="Petabridge, LLC">
//      Copyright (C) 2021 - 2021 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace Petabridge.Cmd.Cluster.Sharding.Repair
{
    internal static class ClusterShardingRepairCmd
    {
        public static readonly CommandDefinition PrintInternalClusterShardingData = new CommandDefinitionBuilder()
            .WithName("print-sharding-data")
            .WithDescription(
                "Lists all of the Akka.Persistence entity ids that will be deleted by Akka.Cluster.Sharding")
            .Build();
        
        public static readonly CommandDefinition PrintShardRegionNameData = new CommandDefinitionBuilder()
            .WithName("print-sharding-regions")
            .WithDescription(
                "Lists all of the shardRegion names currently stored inside Akka.Cluster.Sharding persistence storage.")
            .Build();
        
        public static readonly CommandDefinition RemoveInternalClusterShardingData = new CommandDefinitionBuilder()
            .WithName("delete-sharding-data")
            .WithDescription("Delete all Akka.Cluster.Sharding regions known to the current node.")
            .WithArgument(b => b.WithName("typeName")
                .WithSwitch("-t").WithSwitch("-T").WithDescription("The name of the entity type.")
                .AllowMultiple(true)
                .IsMandatory(true))
            .WithArgument(b => b.WithName("journalPluginId")
                .WithSwitch("-j").WithSwitch("-J").WithDescription("The cluster sharding persistent journal plugin ID.")
                .AllowMultiple(false)
                .IsMandatory(false))
            .WithArgument(b => b.WithName("snapshotPluginId")
                .WithSwitch("-s").WithSwitch("-S").WithDescription("The cluster sharding persistent snapshot store plugin ID.")
                .AllowMultiple(false)
                .IsMandatory(false))
            .Build();
        
        public static readonly CommandPalette ClusterShardingRepairCommandPalette = new CommandPalette("cluster-sharding-repair",
            new[] {RemoveInternalClusterShardingData, PrintShardRegionNameData, PrintInternalClusterShardingData });
    }
}