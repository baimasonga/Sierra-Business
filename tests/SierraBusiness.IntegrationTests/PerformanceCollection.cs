using Xunit;

namespace SierraBusiness.IntegrationTests;

// Wall-clock budgets need an idle test runner, rather than competing with the
// rest of the database and rendering tests for CPU and disk access.
[CollectionDefinition("Production readiness performance", DisableParallelization = true)]
public sealed class PerformanceCollection;
