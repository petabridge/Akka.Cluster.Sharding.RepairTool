using System;
using System.IO;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Query.Sql;
using Akka.Streams.Dsl;
using Petabridge.Cmd.Cluster.Sharding.Repair;

namespace RepairTool
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            /*
             
            // IN A PRODUCTION SYSTEM, you could install Akka.Persistence.SqlServer (or any SQL plugin)
            // and the Akka.Persistence.Query.Sql NuGet package and call the following:

            Func<ActorSystem, ICurrentPersistenceIdsQuery> queryMapper = actorSystem =>
            {
                var pq = PersistenceQuery.Get(actorSystem);
                var readJournal = pq.ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier);
                
                // works because `SqlReadJournal` implements `ICurrentPersistenceIdsQuery`, among other
                // Akka.Persistence.Query interfaces
                return readJournal;
            };
            
            */
            
            Func<ActorSystem, ICurrentPersistenceIdsQuery> queryMapper = actorSystem =>
            {
                // TODO: REPLACE THIS
                // SEE THE DOCUMENTATION: https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool
                return new PlaceholderReadJournal();
            };
            
            var config = ConfigurationFactory.ParseString(File.ReadAllText("app.conf"));

            var repairRunner = new RepairRunner();

            await repairRunner.Start(queryMapper, config);
            await repairRunner.WaitForShutdown();
        }
    }
   
}
