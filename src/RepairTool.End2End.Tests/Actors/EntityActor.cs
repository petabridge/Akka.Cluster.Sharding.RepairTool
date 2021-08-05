using System;
using Akka;
using Akka.Persistence;
using Akka.Actor;
using Akka.Cluster.Sharding;

namespace RepairTool.End2End.Tests.Actors
{
    public class EntityActor : ReceivePersistentActor
    {
        public EntityActor(string persistenceId)
        {
            PersistenceId = persistenceId + Guid.NewGuid();
            
            Command<string>(str =>
            {
                Persist(str, s =>
                {
                    Sender.Tell("ack");
                });
            });
        }

        public override string PersistenceId { get; }
    }

    public sealed class ShardEnvelope
    {
        public ShardEnvelope(string entityId, string message)
        {
            EntityId = entityId;
            Message = message;
        }

        public string EntityId { get; }
        
        public string Message { get; }
    }

    public sealed class MessageRouter : HashCodeMessageExtractor
    {
        public MessageRouter() : this(10)
        {
        }
        
        public MessageRouter(int maxNumberOfShards) : base(maxNumberOfShards)
        {
        }

        public override string EntityId(object message)
        {
            if (message is ShardEnvelope e)
            {
                return e.EntityId;
            }

            return null;
        }

        public override object EntityMessage(object message)
        {
            if (message is ShardEnvelope e)
            {
                return e.Message;
            }

            return null;
        }
    }
}