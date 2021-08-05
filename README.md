# Akka.Cluster.Sharding RepairTool

This project is a commandline repair tool for [Akka.NET](https://getakka.net/) users who:

1. Are using [Akka.Cluster.Sharding](https://getakka.net/articles/clustering/cluster-sharding.html);
2. Are using sharding with `akka.cluster.sharding.state-store-mode=persistence`, currently the default as of Akka.NET v1.4+; and
3. Have ran into situations where the entire cluster goes offline ungracefully and are thus left with "artifacts" of the previous cluster's state in their Akka.Persistence data, which prevents Akka.Cluster.Sharding from starting up correctly and placing all of the `ShardRegion` actors.

> **N.B.** You can avoid having to use this tool at all by changing your Akka.Cluster.Sharding settings to `akka.cluster.sharding.state-store-mode=ddata` - which uses [Akka.Cluster.DistributedData's in-memory replication](https://getakka.net/articles/clustering/distributed-data.html) to track this data instead.

## Caveat Emptor 
This tool:

* Will delete _all data_ that belongs to the built-in Akka.Cluster.Sharding actors, i.e. those with persistence ids `/system/sharding`;
* Will _not_ delete any of your entity data;
* SHOULD NEVER BE RUN AGAINST A LIVE, RUNNING CLUSTER; and
* **You should really, really read the source code and the instructions before you attempt to use this tool**.

You only need this tool when your `ShardRegionCoordinator` actors die suddenly before they have a chance to cleanup their data. This is a relatively rare occurrence in practice but it does happen. 

**This tool should not be a part of your standard CI/CD processes**. It is to be used in the case of disaster recovery / cluster corruption only.

Once more, for repetition - **this tool should never be used in automated deployments**. You only need it in rare cases where the sharding system terminated abruptly, i.e. a process or hardware failure.

## How to Use `RepairTool`
Akin to what many users do with [Lighthouse](https://github.com/petabridge/lighthouse), this project is designed to be consumed by users by **[cloning this repository](https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool)** as the first step. 

### Build and Configure `RepairTool`
This is because, due to the nature of how Akka.Persistence plugins are highly extensible, configuration-driven, and _store end-user data_ we thought it safest for the end-user to be in control of those dependencies. That isn't going to change - don't submit an issue asking us to.

To get started with `RepairTool`, do the following:

1. [Clone this repository](https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool);
2. Install your specific [Akka.Persistence](https://getakka.net/articles/persistence/architecture.html) plugins that you use with Akka.Cluster.Sharding into the [`RepairTool` project](https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool/tree/dev/src/RepairTool);
3. Add your connection string and Akka.Persistence configuration data to [`app.conf`](https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool/blob/dev/src/RepairTool/app.conf) - _follow the instructions of your specific Akka.Persistence plugin on how to do this_;
4. As a final step, you need to replace [`Func<ActorSystem, ICurrentPersistenceIdsQuery>` block of code in `Program.cs`](https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool/blob/039caf6899b87a88e29a37af80ec0425b654246b/src/RepairTool/Program.cs#L35-L40) with your own Akka.Persistence plugin's code for retreiving the [`IReadJournal`](https://getakka.net/api/Akka.Persistence.Query.IReadJournal.html). You only really need to do this step if you are interested in being able to preview what data is going to be removed _before_ you remove it. The actual repair commands don't depend on it.

#### Examples of `ICurrentPersistenceIdsQuery` Mapping
With the notable exception of Akka.Persistence.Redis [we are planning on fixing that](https://github.com/akkadotnet/Akka.Persistence.Redis/issues/158) and Akka.Persistence.Azure, [which we are also planning on fixing](https://github.com/petabridge/Akka.Persistence.Azure/issues/130), all actively maintained Akka.Persistence plugins support Akka.Persistence.Query and the `ICurrentPersistenceIdsQuery` method specifically.

Here are some examples of how to set it up:

##### Akka.Persistence.SqlServer
For SQL packages, you have to install a second NuGet package to get Akka.Persistence.Query support:
```shell
PS> Install-Package Akka.Persistence.Query.Sql
```

```csharp
Func<ActorSystem, ICurrentPersistenceIdsQuery> queryMapper = actorSystem =>
{
    var pq = PersistenceQuery.Get(actorSystem);
    var readJournal = pq.ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier);
    
    // works because `SqlReadJournal` implements `ICurrentPersistenceIdsQuery`, among other
    // Akka.Persistence.Query interfaces
    return readJournal;
};
```

##### Akka.Persistence.MongoDb

```csharp
Func<ActorSystem, ICurrentPersistenceIdsQuery> queryMapper = actorSystem =>
    actorSystem.ReadJournalFor<MongoDbReadJournal>(MongoDbReadJournal.Identifier);
```

##### Akka.Persistence.Azure

**Runs, but doesn't work properly yet and is being fixed**: https://github.com/petabridge/Akka.Persistence.Azure/issues/130

```csharp
Func<ActorSystem, ICurrentPersistenceIdsQuery> queryMapper = actorSystem =>
    actorSystem.ReadJournalFor<AzureTableStorageReadJournal>(AzureTableStorageReadJournal.Identifier);
```

**Please feel free to send along additional PRs to update this list**.

### Compilation
After you have all of your code and configuration setup, it's time to produce your binaries.

> `RepairTool` requires .NET 5 to build and run.

In the root of the folder where you cloned this repository, run:

```shell
PS> build.cmd Docker
```

This will:

* Create a local Docker image called `repairtool:latest` and `repairtool:{currentVersion}` and
* Create a binary deployable version of `RepairTool.dll` in `/src/RepairTool/bin/Release/net5/publish/`.

You can use either of these to run `RepairTool`.

### Running `RepairTool`
Once you have compiled your application and configured everything correctly, it's time to run `RepairTool`.

`RepairTool` works using a custom [Petabridge.Cmd](https://cmd.petabridge.com/) palette that is included with the source code in this repository.

This palette has the following commands (which you can discover via tab-autocompletion if using the [`pbm` CLI](https://cmd.petabridge.com/articles/install/index.html))

* `cluster-sharding-repair print-sharding-regions` - lists the names of all of the `ShardRegion`s found inside the current Akka.Persistence connection.
* `cluster-sharding-repair print-sharding-data` - lists all of the raw persistenceIds that belong to the Akka.Cluster.Sharding system.
* `cluster-sharding-repair delete-sharding-data -t {regionName1} -t {regionName2}` - **the actual repair command**; deletes the data from both the Akka.Persistence journal and snapshot store for these `ShardRegion`s.

By default `ReparTool` hosts its `Petabridge.Cmd` TCP port on port 9777, so you can do the following to run it:

```shell
docker run --name shardrepair -p 9777:9777 repairtool
```

or if you want to run it directly on your developer machine without Docker:

```shell
cd src/RepairTool 
dotnet run -c Release
```

Next, we just connect to it via `pbm` and run these commands:

```shell
pbm localhost:9777 cluster-sharding-repair print-sharding-regions
pbm localhost:9777 cluster-sharding-repair delete-sharding-data -t {regionName1} -t {regionName2}
```

And that should purge all of the entity data that belongs to the `/system/sharding` actors only. You'll see the output on the CLI as it's written.

## Support
If you need support or help using this tool in practice, please [purchase an Akka.NET Support Plan](https://petabridge.com/services/support/). 

© 2015-2021 Petabridge