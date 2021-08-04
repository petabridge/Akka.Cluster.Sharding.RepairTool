using Akka.Util.Internal;

namespace RepairTool.End2End.Tests
{
    /// <summary>
    /// Utility class used to help version databases for multiple specs
    /// </summary>
    public static class RedisDatabaseCounter
    {
        private static readonly AtomicCounter Counter = new AtomicCounter(0);

        public static int Next()
        {
            return Counter.IncrementAndGet();
        }
    }
}