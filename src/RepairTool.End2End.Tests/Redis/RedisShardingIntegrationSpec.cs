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

namespace RepairTool.End2End.Tests.Redis
{
    [Collection("RedisSpec")]
    public class RedisShardingIntegrationSpec : TestKit
    {
        public static Config Config(RedisFixture fixture, int id)
        {
            return ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                configuration-string = ""{fixture.ConnectionString}""
                database = {id}
            }}
            akka.persistence.snapshot-store {{
                        plugin = ""akka.persistence.snapshot-store.redis""
                        redis {{
                            class = ""Akka.Persistence.Redis.Snapshot.RedisSnapshotStore, Akka.Persistence.Redis""
                            configuration-string = ""{fixture.ConnectionString}""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            database = ""{id}""
                        }}
                    }}");
        }

        public static Func<ActorSystem, ICurrentPersistenceIdsQuery> QueryMapper = actorSys => actorSys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);

        public static ActorSystemSetup CreateSetup(RedisFixture fixture, int id)
        {
            var serviceCollection = new ServiceCollection();
            var config = Config(fixture, id);
            var bootstrap = BootstrapSetup.Create().WithConfig(config).WithActorRefProvider(ProviderSelection.Cluster.Instance);
            return bootstrap.And(DependencyResolverSetup.Create(serviceCollection.BuildServiceProvider()));
        }

        public RedisFixture Fixture { get; }
        
        /// <summary>
        /// Need this so we don't include all of the Akka.Cluster/Akka.Remote stuff in the setup for our RepairRunner
        /// </summary>
        public Config AkkaPersistenceConfig { get; }
        
        public RedisShardingIntegrationSpec(ITestOutputHelper output, RedisFixture fixture) : this(output, fixture, RedisDatabaseCounter.Next())
        {
        }
        
        protected RedisShardingIntegrationSpec(ITestOutputHelper output, RedisFixture fixture, int databaseId) : base(CreateSetup(fixture, databaseId), output: output)
        {
            Fixture = fixture;
            AkkaPersistenceConfig = Config(fixture, databaseId);
        }
        
        [Fact(Skip = "CurrentPersistenceIds not supported in Akka.Persistence.Redis v1.4.20")]
        public async Task ShardingCreateAndPurgeRedisSpec()
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
            Within(TimeSpan.FromSeconds(20), () =>
            {
                ReceiveN(count * shardRegions.Length);
            });
            
            // terminate host node - simulate total cluster shutdown
            await Sys.Terminate();
            
            var runner = new RepairRunner();
            using var cts = new CancellationTokenSource();

            /* END ARRANGE */

            try
            {
                /* BEGIN ACT */
                
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
                var logger = actorSystem.As<ExtendedActorSystem>().SystemActorOf(Props.Create(() => new TestOutputLogger(Output)), "log-test");
                logger.Tell(new InitializeLogger(actorSystem.EventStream));
                
                // query the sharding persistent ids
                var pbm = svc.Cmd;
                var clientActual = await pbm.StartLocalClient(cts.Token);

                var materializer = actorSystem.Materializer();
                
                var session1 = await clientActual.ExecuteTextCommandAsync("cluster-sharding-repair print-sharding-regions", cts.Token);
                var sink1 = Sink.Seq<CommandResponse>();
                var outputFlow = Flow.Create<CommandResponse>().Select(s =>
                {
                    Output.WriteLine(s.ToString());
                    return s;
                });

                var responses = await session1.Stream.Via(outputFlow).Where(x => !x.Final).RunWith(sink1, materializer);
                responses.Select(x => x.Msg).Should().BeEquivalentTo(shardRegions);

                /* END ACT */
            }
            finally
            {
                cts.Cancel();
                await runner.StopAsync();
            }    
        }
    }
}