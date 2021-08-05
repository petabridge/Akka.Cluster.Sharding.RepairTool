using System;
using Akka;
using Akka.Persistence.Query;
using Akka.Streams.Dsl;

namespace Petabridge.Cmd.Cluster.Sharding.Repair
{
    /// <summary>
    /// Used to make the compiler happy, but still blow up if the end-user did it wrong.
    /// </summary>
    public sealed class PlaceholderReadJournal : ICurrentPersistenceIdsQuery
    {
        public Source<string, NotUsed> CurrentPersistenceIds()
        {
            return Source.Failed<string>(new NotImplementedException(
                "This is a placeholder ReadJournal implementation. Replace it with a real one in your service configuration"));
        }
    }
}