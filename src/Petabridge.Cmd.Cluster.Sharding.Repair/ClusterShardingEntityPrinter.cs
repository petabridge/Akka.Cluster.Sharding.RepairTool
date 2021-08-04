using System;
using System.Linq;
using Akka;
using Akka.Actor;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.Dsl;

namespace Petabridge.Cmd.Cluster.Sharding.Repair
{
    /// <summary>
    /// Responsible for printing out all of the Akka.Persistence entities detected inside the ShardRegion
    /// </summary>
    internal class ClusterShardingEntityPrinter : ReceiveActor
    {
        private class PrintComplete
        {
            public static readonly PrintComplete Instance = new PrintComplete();
            private PrintComplete(){}
        }

        private readonly IActorRef _reporter;
        private readonly IPersistenceIdsQuery _readJournal;
        private bool _regionsOnly;

        public ClusterShardingEntityPrinter(IPersistenceIdsQuery readJournal, bool regionsOnly, IActorRef reporter)
        {
            _readJournal = readJournal;
            _regionsOnly = regionsOnly;
            _reporter = reporter;

            Receive<string>(str =>
            {
                if (_regionsOnly && str.Contains("Coordinator"))
                {
                    var (startPos, endPos) = (str.IndexOf("/system/sharding/", StringComparison.Ordinal), str.IndexOf("Coordinator", StringComparison.Ordinal));
                    var regionName = str.Substring(startPos, endPos);
                    _reporter.Tell(new CommandResponse(regionName, final:false));
                    return;
                }
                
                _reporter.Tell(new CommandResponse(str));
            });

            Receive<PrintComplete>(_ =>
            {
                _reporter.Tell(CommandResponse.Empty);
                Context.Stop(Self);
            });
        }

        protected override void PreStart()
        {
            var source = _readJournal.PersistenceIds().Where(x => x.StartsWith("/system/sharding"));
            var sink = Sink.ActorRef<string>(Self, PrintComplete.Instance);
            source.RunWith(sink, Context.Materializer());
        }

        protected override void PreRestart(Exception reason, object message)
        {
            _reporter.Tell(new CommandResponse("Errored. Restarting..."));
            base.PreRestart(reason, message);
        }
    }
}