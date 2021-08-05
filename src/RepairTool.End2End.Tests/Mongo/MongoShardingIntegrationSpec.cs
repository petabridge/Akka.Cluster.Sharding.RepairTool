using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Cluster.Sharding;
using Akka.Configuration;
using Akka.DependencyInjection;
using Akka.Event;
using Akka.Persistence.MongoDb.Query;
using Akka.Persistence.Query;
using Akka.Persistence.Redis.Query;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using Akka.TestKit.Xunit2.Internals;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Petabridge.Cmd;
using Petabridge.Cmd.Cluster.Sharding.Repair;
using RepairTool.End2End.Tests.Actors;
using Xunit;
using Xunit.Abstractions;
using Envelope = Akka.Actor.Envelope;

namespace RepairTool.End2End.Tests.Mongo
{
    [Collection("MongoSpec")]
    public class MongoShardingIntegrationSpec : TestKit
    {
        public static Config Config(MongoFixture fixture, int id)
        {
            return ConfigurationFactory.ParseString($@"
            akka.test.single-expect-default = 10s
            akka.persistence {{
                publish-plugin-commands = on
                journal {{
                    plugin = ""akka.persistence.journal.mongodb""
                    mongodb {{
                        class = ""Akka.Persistence.MongoDb.Journal.MongoDbJournal, Akka.Persistence.MongoDb""
                        connection-string = ""{fixture.ConnectionString + id}""
                        auto-initialize = on
                        collection = ""EventJournal""
                    }}
                }}
                 snapshot-store {{
                    plugin = ""akka.persistence.snapshot-store.mongodb""
                    mongodb {{
                        class = ""Akka.Persistence.MongoDb.Snapshot.MongoDbSnapshotStore, Akka.Persistence.MongoDb""
                        connection-string =  ""{fixture.ConnectionString + id}""
                        auto-initialize = on
                        collection = ""SnapshotStore""
                    }}
                }}
                query {{
                    mongodb {{
                        class = ""Akka.Persistence.MongoDb.Query.MongoDbReadJournalProvider, Akka.Persistence.MongoDb""
                        refresh-interval = 1s
                    }}
                }}
            }}");
        }

        public static Func<ActorSystem, ICurrentPersistenceIdsQuery> QueryMapper = actorSys =>
            actorSys.ReadJournalFor<MongoDbReadJournal>(MongoDbReadJournal.Identifier);

        public static ActorSystemSetup CreateSetup(MongoFixture fixture, int id)
        {
            var serviceCollection = new ServiceCollection();
            var config = Config(fixture, id);
            var bootstrap = BootstrapSetup.Create().WithConfig(config)
                .WithActorRefProvider(ProviderSelection.Cluster.Instance);
            return bootstrap.And(DependencyResolverSetup.Create(serviceCollection.BuildServiceProvider()));
        }

        public MongoFixture Fixture { get; }

        /// <summary>
        /// Need this so we don't include all of the Akka.Cluster/Akka.Remote stuff in the setup for our RepairRunner
        /// </summary>
        public Config AkkaPersistenceConfig { get; }

        public MongoShardingIntegrationSpec(ITestOutputHelper output, MongoFixture fixture) : this(output, fixture,
            RedisDatabaseCounter.Next())
        {
        }

        protected MongoShardingIntegrationSpec(ITestOutputHelper output, MongoFixture fixture, int databaseId) : base(
            CreateSetup(fixture, databaseId), output: output)
        {
            Fixture = fixture;
            AkkaPersistenceConfig = Config(fixture, databaseId);
        }

        [Fact]
        public async Task ShardingCreateAndPurgeMongoSpec()
        {
            var shardRegions = new[] {"typeA", "typeB"};

            /* BEGIN ARRANGE */

            // form a single node cluster
            var cluster = Akka.Cluster.Cluster.Get(Sys);
            await cluster.JoinAsync(cluster.SelfAddress);
            await AwaitAssertAsync(() => cluster.State.Members.Count.Should().Be(1));

            // start sharding system
            var sharding = ClusterSharding.Get(Sys);

            var shardRegion1 = sharding.Start(shardRegions[0], s => Props.Create(() => new EntityActor(s)),
                ClusterShardingSettings.Create(Sys), new MessageRouter());

            var shardRegion2 = sharding.Start(shardRegions[1], s => Props.Create(() => new EntityActor(s)),
                ClusterShardingSettings.Create(Sys), new MessageRouter());

            var count = 100;
            foreach (var i in Enumerable.Range(0, count))
            {
                var s = i.ToString();
                var e = new ShardEnvelope(s, s);
                shardRegion1.Tell(e);
                shardRegion2.Tell(e);
            }

            // receive 200 acks - should be enough to create ~10 shards for all sets of entities
            Within(TimeSpan.FromSeconds(20), () => { ReceiveN(count * shardRegions.Length); });

            // terminate host node - simulate total cluster shutdown
            await Sys.Terminate();

            var runner = new RepairRunner();
            
            // two minutes to run everything
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            /* END ARRANGE */

            try
            {
                // start running in background without console lifetime
                await runner.Start(QueryMapper, AkkaPersistenceConfig, cts.Token, useConsoleLifetime:false);

                // wait for Pbm to start
                await AwaitAssertAsync(async () =>
                {
                    var pbm = runner.ServiceProvider.GetRequiredService<IPbmClientService>().Cmd;
                    var client = await pbm.StartLocalClient(cts.Token);
                    var palettes = await client.GetAvailablePalettes();

                    // verify that the repair palette is defined in the ActorSystem in the repair process
                    palettes.Should().Contain(x =>
                        x.ModuleName.Equals(ClusterShardingRepairCmd.ClusterShardingRepairCommandPalette.ModuleName));
                });

                var svc = runner.ServiceProvider.GetRequiredService<IPbmClientService>();
                var actorSystem = svc.Sys;

                // add Output logger to system
                var logger = actorSystem.As<ExtendedActorSystem>()
                    .SystemActorOf(Props.Create(() => new TestOutputLogger(Output)), "log-test");
                logger.Tell(new InitializeLogger(actorSystem.EventStream));

                // query the sharding persistent ids
                var pbm = svc.Cmd;
                var clientActual = await pbm.StartLocalClient(cts.Token);

                var materializer = actorSystem.Materializer();

                var session1 =
                    await clientActual.ExecuteTextCommandAsync("cluster-sharding-repair print-sharding-regions",
                        cts.Token);
                var sink1 = Sink.Seq<CommandResponse>();
                var outputFlow = Flow.Create<CommandResponse>().Select(s =>
                {
                    Output.WriteLine(s.ToString());
                    return s;
                });

                var responses1 = await session1.Stream.Via(outputFlow).Where(x => !x.Final).RunWith(sink1, materializer);
                responses1.Select(x => x.Msg).Should().BeEquivalentTo(shardRegions);
                
                // query all shard persistent ids
                var session2 = await clientActual.ExecuteTextCommandAsync(
                    "cluster-sharding-repair print-sharding-data",
                    cts.Token);
                
                var sink2 = Sink.Seq<CommandResponse>();

                var responses2 = await session2.Stream.Via(outputFlow).Where(x => !x.Final)
                    .RunWith(sink2, materializer);

                // the raw persistent ids belonging to Cluster.Sharding should be formatted correctly
                responses2.Select(x => x.Msg).All(x => x.StartsWith("/system/sharding")).Should().BeTrue();
                
                // time to delete all of our raw data
                var session3 = await clientActual.ExecuteTextCommandAsync(
                    $"cluster-sharding-repair delete-sharding-data {string.Join(" ", shardRegions.Select(c => $"-t {c}"))}", cts.Token);
                
                var sink3 = Sink.Seq<CommandResponse>();
                
                var responses3 = await session3.Stream.Via(outputFlow)
                    .RunWith(sink3, materializer);

                // should have completed successfully
                responses3.Any(x => x.IsError).Should().BeFalse();
                
                // re-run shardRegion query - should return nothing
                // manually query event data for one of our shard regions and see if anything is left
                var readjournal = actorSystem.ReadJournalFor<MongoDbReadJournal>(MongoDbReadJournal.Identifier);
                var sink4 = Sink.Seq<EventEnvelope>();
                var response4 = await readjournal.CurrentEventsByPersistenceId(responses2[0].Msg, 0L, Int64.MaxValue)
                    .RunWith(sink4, materializer);

                response4.Count.Should().Be(0);

            }
            finally
            {
                cts.Cancel();
                await runner.StopAsync();
            }
        }
    }
}