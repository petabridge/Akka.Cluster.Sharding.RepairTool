using System;
using Mongo2Go;
using Xunit;

namespace RepairTool.End2End.Tests.Mongo
{
    [CollectionDefinition("MongoSpec")]
    public sealed class MongoSpecsFixture : ICollectionFixture<MongoFixture>
    {
    }
    
    public class MongoFixture : IDisposable
    {
        private MongoDbRunner _runner;

        public string ConnectionString { get; private set; }

        public MongoFixture()
        {
            _runner = MongoDbRunner.Start();
            ConnectionString = _runner.ConnectionString + "akkanet";
        }

        public void Dispose()
        {
            _runner.Dispose();
        }
    }
}