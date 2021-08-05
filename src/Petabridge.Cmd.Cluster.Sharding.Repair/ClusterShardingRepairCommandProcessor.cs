// -----------------------------------------------------------------------
// <copyright file="ShardingRepairCommandProcessor.cs" company="Petabridge, LLC">
//      Copyright (C) 2021 - 2021 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster;
using Akka.Event;
using Akka.Pattern;
using Akka.Persistence;
using Akka.Util;

namespace Petabridge.Cmd.Cluster.Sharding.Repair
{
    internal class ClusterShardingRepairCommandProcessor : ReceiveActor
    {
        internal class RemoveOnePersistenceId : PersistentActor
        {
            public static Props Props(string journalPluginId, string snapshotPluginId, string persistenceId, IActorRef replyTo) =>
                Akka.Actor.Props.Create(() => new RemoveOnePersistenceId(journalPluginId, snapshotPluginId, persistenceId, replyTo));
            
            public class Removals
            {
                public Removals(bool events, bool snapshots)
                {
                    Events = events;
                    Snapshots = snapshots;
                }

                public bool Events { get; }
                public bool Snapshots { get; }
                
            }
            
            public class Result
            {
                public Result(Try<Removals> removals)
                {
                    Removals = removals;
                }

                public Try<Removals> Removals { get; }
            }
            
            private readonly IActorRef _replyTo;

            private bool _hasSnapshots = false;
            
            public RemoveOnePersistenceId(string journalPluginId, string snapshotPluginId, string persistenceId, IActorRef replyTo)
            {
                JournalPluginId = journalPluginId;
                SnapshotPluginId = snapshotPluginId; 
                PersistenceId = persistenceId;
                _replyTo = replyTo;
            }
            
            public override string PersistenceId { get; }

            public override Recovery Recovery { get; } = Recovery.None;

            protected override bool ReceiveRecover(object message)
            {
                switch (message)
                {
                    case ClusterEvent.IClusterDomainEvent _:
                        return true;
                    
                    case SnapshotOffer _:
                        _hasSnapshots = true;
                        return true;
                    
                    case RecoveryCompleted _:
                        DeleteMessages(long.MaxValue);
                        if(_hasSnapshots)
                            DeleteSnapshots(new SnapshotSelectionCriteria(long.MaxValue, DateTime.MaxValue, 0, DateTime.MinValue));
                        else
                            Context.Become(WaitDeleteMessagesSuccess);
                        return true;
                    
                    default:
                        return false;
                }
            }

            protected override bool ReceiveCommand(object message)
            {
                switch (message)
                {
                    case DeleteSnapshotsSuccess _:
                        Context.Become(WaitDeleteMessagesSuccess);
                        return true;
                    
                    case DeleteMessagesSuccess _:
                        Context.Become(WaitDeleteSnapshotsSuccess);
                        return true;
                    
                    default:
                        return HandleFailure(message);
                }
            }

            private bool WaitDeleteSnapshotsSuccess(object message)
            {
                switch (message)
                {
                    case DeleteSnapshotsSuccess _:
                        Done();
                        return true;
                    
                    default:
                        return HandleFailure(message);
                }
            }

            private bool WaitDeleteMessagesSuccess(object message)
            {
                switch (message)
                {
                    case DeleteMessagesSuccess _:
                        Done();
                        return true;
                    
                    default:
                        return HandleFailure(message); 
                }
            }

            private bool HandleFailure(object message)
            {
                switch (message)
                {
                    case DeleteMessagesFailure fail:
                        Failure(fail.Cause);
                        return true;
                    
                    case DeleteSnapshotFailure fail:
                        Failure(fail.Cause);
                        return true;
                    
                    default:
                        return false;
                }
            }

            private void Done()
            {
                _replyTo.Tell(new Result(new Try<Removals>(new Removals(LastSequenceNr > 0, _hasSnapshots))));
                Context.Stop(Self);
            }
            
            private void Failure(Exception cause)
            {
                _replyTo.Tell(new Result(new Try<Removals>(cause)));
                Context.Stop(Self);
            }
        }

        public static Props Props(
            string journalPluginId,
            string snapshotPluginId,
            HashSet<string> typeName,
            IActorRef replyTo)
            => Akka.Actor.Props.Create(() => 
                    new ClusterShardingRepairCommandProcessor(journalPluginId, snapshotPluginId, typeName, replyTo))
                .WithDeploy(Deploy.Local);
        
        private readonly ILoggingAdapter _log;
        private readonly string _journalPluginId;
        private readonly string _snapshotPluginId;
        private readonly Queue<string> _remainingPid;
        private readonly IActorRef _replyTo;

        private string _currentPid;
        private IActorRef _currentRef;
        
        public ClusterShardingRepairCommandProcessor(
            string journalPluginId, 
            string snapshotPluginId,
            HashSet<string> typeNames, 
            IActorRef replyTo)
        {
            _journalPluginId = journalPluginId;
            _snapshotPluginId = snapshotPluginId;
            _replyTo = replyTo;
            _log = Context.GetLogger();

            _remainingPid = new Queue<string>(typeNames.Select(PersistenceId));

            Receive<RemoveOnePersistenceId.Result>(result =>
            {
                if (result.Removals.IsSuccess)
                {
                    var msg = $"Removed data for persistenceId [{_currentPid}]";
                    _log.Info(msg);
                    _replyTo.Tell(new CommandResponse(msg, false));
                    if (_remainingPid.Count == 0)
                    {
                        _replyTo.Tell(CommandResponse.Empty);
                        Context.Stop(Self);
                    }
                    else
                        RemoveNext();
                }
                else
                {
                    var cause = result.Removals.Failure.Value;
                    var msg = $"Failed to remove data for persistenceId [{_currentPid}]. Exception: [{cause}]";
                    _log.Error(cause, msg);
                    _replyTo.Tell(new ErroredCommandResponse(msg));
                    Context.Stop(Self);
                }
            });

            Receive<Terminated>(terminated =>
            {
                try
                {
                    if (terminated.ActorRef.Equals(_currentRef))
                        throw new IllegalStateException(
                            $"Failed to remove data for persistenceId [{_currentPid}], unexpected termination.");
                }
                catch (IllegalStateException ex)
                {
                    _log.Error(ex, ex.Message);
                    _replyTo.Tell(new ErroredCommandResponse(ex.Message));
                    Context.Stop(Self);
                }
            });
        }

        public static string PersistenceId(string typeName) => $"/system/sharding/{typeName}Coordinator/singleton/coordinator";

        protected override void PreStart()
        {
            RemoveNext();
        }

        private void RemoveNext()
        {
            _currentPid = _remainingPid.Dequeue();
            var msg = $"Removing data for persistenceId [{_currentPid}]";
            _log.Info(msg);
            _replyTo.Tell(new CommandResponse(msg, false));
            _currentRef = Context.ActorOf(RemoveOnePersistenceId.Props(_journalPluginId, _snapshotPluginId, _currentPid, Self));
            Context.Watch(_currentRef);
        }
    }
}