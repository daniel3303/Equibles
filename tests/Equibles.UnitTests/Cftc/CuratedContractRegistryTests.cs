using Equibles.Cftc.HostedService.Services;

namespace Equibles.UnitTests.Cftc;

public class CuratedContractRegistryTests {
    [Fact]
    public void Contracts_MarketCodes_AreUniqueCaseInsensitive() {
        // CftcImportService.Import builds the curated lookup with
        //     CuratedContractRegistry.Contracts.ToDictionary(c => c.MarketCode.Trim(), StringComparer.OrdinalIgnoreCase)
        // If two entries share a MarketCode (case-insensitive, trimmed), that call throws
        // ArgumentException at the very start of every CFTC import — the worker catches it
        // at the per-year boundary and reports an error, but no COT data ever lands. The
        // failure is silent because the worker also catches it on cycle and sleeps; you only
        // notice when the dashboard shows stale data. Pin the uniqueness invariant so a
        // copy-paste mistake adding a duplicate market code is caught at test time, not in
        // production logs days later.
        var duplicates = CuratedContractRegistry.Contracts
            .GroupBy(c => c.MarketCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        duplicates.Should().BeEmpty();
    }
}
