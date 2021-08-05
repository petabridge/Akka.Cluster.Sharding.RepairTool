using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Persistence.Sqlite;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Petabridge.Cmd.Cluster.Sharding.Repair.Tests
{
    public class ShardingRepairSpec : TestKit, IAsyncLifetime
    {
        private static Config Config = ConfigurationFactory.ParseString(@"
akka {
    actor {
        provider = cluster
        serializers {
            hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
        }
        serialization-bindings {
            ""System.Object"" = hyperion
        }
    }
    remote {
        dot-netty.tcp {
            public-hostname = localhost
            hostname = localhost
            port = 6666
        }
    }
    cluster {
        auto-down-unreachable-after = 5s
        sharding {
            least-shard-allocation-strategy.rebalance-threshold = 3
            remember-entities = on
        }
        seed-nodes = [""akka.tcp://sharded-cluster-system@localhost:6666""]
    }
    persistence {
        journal {
            plugin = ""akka.persistence.journal.sqlite""
            sqlite {
                connection-string = ""Datasource=store.db""
                auto-initialize = true
            }
        }
        snapshot-store {
            plugin = ""akka.persistence.snapshot-store.sqlite""
            sqlite {
                connection-string = ""Datasource=store.db""
                auto-initialize = true
            }
        }
    }
}")
            .WithFallback(SqlitePersistence.DefaultConfiguration())
            .WithFallback(ClusterSharding.DefaultConfig())
            .WithFallback(ClusterSingletonManager.DefaultConfig());
        
        public ShardingRepairSpec(ITestOutputHelper output) : base(Config, "sharded-cluster-system", output)
        { }
        
        [Fact]
        public async Task Should_delete_internal_data()
        {
            var commandHandler = Sys.ActorOf(ClusterShardingRepairCommands.Instance.HandlerProps);
            
            var command = new Command("cluster-sharding-repair", "delete-sharding-data", new[]
            {
                new Tuple<string, string>("-t", "customer")
            });
            commandHandler.Tell(command);

            var done = false;
            IActorRef handler = null;
            while (!done)
            {
                ExpectMsg<CommandResponse>((msg, actor) =>
                {
                    if (handler is null)
                    {
                        handler = actor;
                        Watch(actor);
                    }
                    if(!msg.Equals(CommandResponse.Empty))
                        Output.WriteLine(msg.Msg);
                    done = msg.Final;
                });
            }
            
            var terminated = ExpectMsg<Terminated>();
            handler.Should().NotBeNull();
            terminated.ActorRef.Should().Be(handler);

            var persistenceId = ClusterShardingRepairCommandProcessor.PersistenceId("customer");
            var connStr = Sys.Settings.Config.GetString("akka.persistence.journal.sqlite.connection-string", null);
            var table = Sys.Settings.Config.GetString("akka.persistence.journal.sqlite.table-name", null);
            AssertTableCleaned(connStr, table, persistenceId);
            
            table = Sys.Settings.Config.GetString("akka.persistence.snapshot-store.sqlite.table-name", null);
            AssertTableCleaned(connStr, table, persistenceId);

            await Sys.Terminate();
        }

        private void AssertTableCleaned(string connStr, string table, string persistenceId)
        {
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                using (var cmd = new SqliteCommand("", conn))
                {
                    cmd.CommandText =
                        $"SELECT COUNT(*) FROM {table} WHERE persistence_id = \"{persistenceId}\"";
                    using (var reader = cmd.ExecuteReader())
                    {
                        reader.Read();
                        reader.GetInt32(0).Should().Be(0);
                    }
                }
            }
        }
        
        public async Task InitializeAsync()
        {
            using (var stream =
                GetType().Assembly.GetManifestResourceStream("Petabridge.Cmd.Cluster.Sharding.Repair.Tests.store.db"))
            {
                if (stream is null)
                    throw new TestClassException("Could not obtain the required test data from resource: [store.db]");
                
                if(File.Exists("store.db"))
                    File.Delete("store.db");
                
                using (var fileStream = new FileStream("store.db", FileMode.CreateNew))
                {
                    await stream.CopyToAsync(fileStream);
                    await fileStream.FlushAsync();
                }
            }
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }
}