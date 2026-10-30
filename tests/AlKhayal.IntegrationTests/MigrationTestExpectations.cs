using System.Linq;
using AlKhayal.Data.Database;

namespace AlKhayal.IntegrationTests
{
    internal static class MigrationTestExpectations
    {
        // Assert all embedded migrations applied; avoid stale counts as additive slices land.
        internal static long Count => typeof(Migrator).Assembly.GetManifestResourceNames().LongCount(name => name.EndsWith(".sql",System.StringComparison.Ordinal));
    }
}
