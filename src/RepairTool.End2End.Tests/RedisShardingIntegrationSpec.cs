using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Cluster.Sharding;
using Akka.Configuration;
using Akka.DependencyInjection;
using Akka.Persistence;
using Akka.Persistence.Query;
using Akka.Persistence.Redis;
using Akka.Persistence.Redis.Query;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RepairTool.End2End.Tests.Actors;
using Xunit;
using Xunit.Abstractions;
using Envelope = RepairTool.End2End.Tests.Actors.Envelope;

namespace RepairTool.End2End.Tests
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
            snapshot-store {{
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

        public static ActorSystemSetup CreateSetup(RedisFixture fixture)
        {
            var serviceCollection = new ServiceCollection();
            var config = Config(fixture, RedisDatabaseCounter.Next());
            var bootstrap = BootstrapSetup.Create().WithConfig(config).WithActorRefProvider(ProviderSelection.Cluster.Instance);
            return bootstrap.And(DependencyResolverSetup.Create(serviceCollection.BuildServiceProvider()));
        }
        
        public ITestOutputHelper Output { get; }
        
        public RedisFixture Fixture { get; }
        
        public RedisShardingIntegrationSpec(ITestOutputHelper output, RedisFixture fixture) : base(CreateSetup(fixture), output: output)
        {
            Output = output;
            Fixture = fixture;
        }
        
        [Fact]
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
                var e = new Envelope(s, s);
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

            /* END ARRANGE */ 
        }
    }
}