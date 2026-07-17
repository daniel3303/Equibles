using Equibles.Cftc.HostedService.Services;

namespace Equibles.UnitTests.Cftc;

public class CuratedContractRegistryTests
{
    [Fact]
    public void Contracts_MarketCodes_AreUniqueCaseInsensitive()
    {
        // CftcImportService.Import builds the curated lookup with
        //     CuratedContractRegistry.Contracts.ToDictionary(c => c.MarketCode.Trim(), StringComparer.OrdinalIgnoreCase)
        // If two entries share a MarketCode (case-insensitive, trimmed), that call throws
        // ArgumentException at the very start of every CFTC import — the worker catches it
        // at the per-year boundary and reports an error, but no COT data ever lands. The
        // failure is silent because the worker also catches it on cycle and sleeps; you only
        // notice when the dashboard shows stale data. Pin the uniqueness invariant so a
        // copy-paste mistake adding a duplicate market code is caught at test time, not in
        // production logs days later.
        var duplicates = CuratedContractRegistry
            .Contracts.GroupBy(c => c.MarketCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        duplicates.Should().BeEmpty();
    }

    [Fact]
    public void Contracts_ContainsEminiSP500WithExactCftcMarketCode13874A()
    {
        // CFTC publishes Commitments of Traders position data keyed by exact MarketCode
        // strings — these are immutable identifiers like SEC CIKs. The E-mini S&P 500
        // (CME) market code is "13874A" specifically — the trailing "A" distinguishes
        // it from the full-size S&P 500 contract and is part of the wire format. A
        // typo regression (e.g. "13874" or "13874a" — case-insensitive lookup makes
        // the latter trickier to catch) would cause CftcImportService.ImportYear to
        // never match any rows for this contract code, silently dropping the highest-
        // volume CFTC-reported equity-index future from the import.
        //
        // The risk: E-mini S&P 500 positioning is THE primary signal on the CFTC
        // equity-index dashboard. Speculative long/short open interest in this single
        // contract drives the "speculator positioning" narrative across financial
        // press. Silently losing it would empty out the dashboard's main column
        // while every other contract continues working — exactly the partial-failure
        // mode that operator inspection misses.
        //
        // Pin both the MarketCode literal AND the DisplayName so a regression that
        // typo'd either field is caught. The display name flows into the dashboard's
        // contract-picker dropdown; a regression there would mis-label the contract
        // for analysts.
        var contract = CuratedContractRegistry.Contracts.SingleOrDefault(c =>
            c.MarketCode == "13874A"
        );

        contract.Should().NotBeNull();
        contract!.DisplayName.Should().Be("E-mini S&P 500 (CME)");
    }

    [Theory]
    // Every pair below was verified against the CFTC public reporting API
    // (publicreporting.cftc.gov dataset 6dca-aqww, cftc_contract_market_code →
    // market_and_exchange_names) on 2026-07-17. An earlier registry revision had
    // exactly these ten codes mislabeled — three pairwise swaps (Platinum/Palladium,
    // 2Y/5Y T-Notes, plus the cattle rows) and a shifted currency chain (the code
    // labeled "Japanese Yen" was really British Pound, "British Pound" really Swiss
    // Franc, "Swiss Franc" really Mexican Peso) — which served one market's real COT
    // data under another market's name. Pin code → name so a future edit can't
    // reintroduce a swap: the code is the identity the importer filters by, and a
    // wrong name here mislabels genuine data everywhere downstream.
    [InlineData("057642", "Live Cattle (CME)")]
    [InlineData("061641", "Feeder Cattle (CME)")]
    [InlineData("054642", "Lean Hogs (CME)")]
    [InlineData("073732", "Cocoa (ICE)")]
    [InlineData("033661", "Cotton No. 2 (ICE)")]
    [InlineData("076651", "Platinum (NYMEX)")]
    [InlineData("075651", "Palladium (NYMEX)")]
    [InlineData("042601", "2-Year T-Notes (CBOT)")]
    [InlineData("044601", "5-Year T-Notes (CBOT)")]
    [InlineData("096742", "British Pound (CME)")]
    [InlineData("092741", "Swiss Franc (CME)")]
    [InlineData("095741", "Mexican Peso (CME)")]
    [InlineData("097741", "Japanese Yen (CME)")]
    public void Contracts_MarketCode_CarriesCftcVerifiedDisplayName(
        string marketCode,
        string expectedDisplayName
    )
    {
        var contract = CuratedContractRegistry.Contracts.SingleOrDefault(c =>
            c.MarketCode == marketCode
        );

        contract.Should().NotBeNull();
        contract!.DisplayName.Should().Be(expectedDisplayName);
    }
}
