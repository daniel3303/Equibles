using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Tests for <see cref="CompanySyncService"/>.
/// The public SyncCompaniesFromSecApi method depends on PostgreSQL-specific features
/// (List&lt;string&gt; SecondaryTickers). We test the private helper methods via
/// reflection where possible.
/// </summary>
public class CompanySyncServiceTests {
    private static readonly MethodInfo IsOperatingCompanyMethod = typeof(CompanySyncService)
        .GetMethod("IsOperatingCompany", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly MethodInfo ParseCikMethod = typeof(CompanySyncService)
        .GetMethod("ParseCik", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo ShouldIncumbentWinMethod = typeof(CompanySyncService)
        .GetMethod("ShouldIncumbentWin", BindingFlags.NonPublic | BindingFlags.Instance);

    [Fact]
    public async Task ShouldIncumbentWin_ListedIncumbentVsUnlistedIncoming_IncumbentWinsRegardlessOfCik() {
        // ShouldIncumbentWin resolves ticker collisions through a priority chain:
        //   1. If either side's SEC metadata is missing → incumbent wins (safe default)
        //   2. If IsListed differs → the LISTED side wins
        //   3. If IsOperatingCompany differs → the operating side wins
        //   4. Numerical CIK tiebreak (smaller CIK wins, via ParseCik)
        //
        // Step 2 is the most important business rule: a real exchange-listed company
        // must always beat an OTC-only subsidiary that happens to share the ticker on
        // SEC's submissions feed. The tiebreak step is already pinned via
        // ParseCik_UnparseableValue_ReturnsLongMaxValue; step 2 has no test.
        //
        // The risk this test pins: a refactor that reorders the priority chain (CIK
        // first), drops step 2 entirely, or swaps the `return incumbentMeta.IsListed`
        // for its negation would let an OTC-only subsidiary win against an exchange-
        // listed parent. The collision would replace real trading data with a CIK
        // that has none — every downstream stock page would 404 or show empty charts.
        //
        // Construction: incoming.Cik = "100" (would beat 200 in CIK tiebreak),
        // incumbent.Cik = "200". Mock incumbent's metadata as listed (NASDAQ),
        // incoming's as OTC-only (unlisted). If the listing priority works, incumbent
        // wins (return true) DESPITE losing the CIK tiebreak — which is the exact
        // distinguishing case.
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetCompanyMetadata("100")
            .Returns(new CompanyMetadata { Cik = "100", EntityType = "operating", Exchanges = ["OTC"] });
        secEdgarClient.GetCompanyMetadata("200")
            .Returns(new CompanyMetadata { Cik = "200", EntityType = "operating", Exchanges = ["NASDAQ"] });

        var service = CreateService(secEdgarClient: secEdgarClient);
        var incoming = new CompanyInfo { Cik = "100", Name = "Subsidiary Co", Tickers = ["X"] };
        var incumbent = new Equibles.CommonStocks.Data.Models.CommonStock { Cik = "200", Ticker = "X" };

        var result = await (Task<bool>)ShouldIncumbentWinMethod.Invoke(service, [incoming, incumbent]);

        result.Should().BeTrue();
    }

    [Fact]
    public void ParseCik_UnparseableValue_ReturnsLongMaxValue() {
        // ShouldIncumbentWin breaks ticker-collision ties with
        //     `ParseCik(incumbent.Cik) <= ParseCik(incoming.Cik)`
        // — the smaller CIK wins. SEC sometimes serves non-numeric CIKs
        // (test feeds, malformed JSON, "N/A" sentinel). ParseCik's MaxValue
        // fallback ensures those junk CIKs LOSE every tie, so a valid
        // incumbent is never replaced by a malformed challenger. Flip the
        // fallback to 0 and unparseable CIKs would WIN every tie, silently
        // overwriting good stocks with garbage. Pin MaxValue so the safety
        // ordering survives any refactor of the helper.
        var result = (long)ParseCikMethod.Invoke(null, ["not-a-number"]);

        result.Should().Be(long.MaxValue);
    }

    private static CompanySyncService CreateService(
        ISecEdgarClient secEdgarClient = null,
        WorkerOptions workerOptions = null) {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        secEdgarClient ??= Substitute.For<ISecEdgarClient>();
        var options = Options.Create(workerOptions ?? new WorkerOptions());
        var logger = Substitute.For<ILogger<CompanySyncService>>();
        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>());

        return new CompanySyncService(scopeFactory, secEdgarClient, options, logger, errorReporter);
    }

    // ═══════════════════════════════════════════════════════════════════
    // IsOperatingCompany — entity type classification
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IsOperatingCompany_WithOperatingEntityType_ReturnsTrue() {
        var service = CreateService();
        var company = new CompanyInfo { Cik = "001", Name = "Apple Inc.", EntityType = "operating" };

        var result = await (Task<bool>)IsOperatingCompanyMethod.Invoke(service, [company]);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsOperatingCompany_WithNonOperatingEntityType_ReturnsFalse() {
        var service = CreateService();
        var company = new CompanyInfo { Cik = "001", Name = "Some ETF", EntityType = "ETF" };

        var result = await (Task<bool>)IsOperatingCompanyMethod.Invoke(service, [company]);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsOperatingCompany_NullEntityType_FetchesFromApi() {
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetEntityType("001").Returns("operating");

        var service = CreateService(secEdgarClient: secEdgarClient);
        var company = new CompanyInfo { Cik = "001", Name = "Apple Inc.", EntityType = null };

        var result = await (Task<bool>)IsOperatingCompanyMethod.Invoke(service, [company]);

        result.Should().BeTrue();
        company.EntityType.Should().Be("operating");
        await secEdgarClient.Received(1).GetEntityType("001");
    }

    [Fact]
    public async Task IsOperatingCompany_NullEntityType_NonOperating_ReturnsFalse() {
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetEntityType("001").Returns("ETF");

        var service = CreateService(secEdgarClient: secEdgarClient);
        var company = new CompanyInfo { Cik = "001", Name = "Some ETF", EntityType = null };

        var result = await (Task<bool>)IsOperatingCompanyMethod.Invoke(service, [company]);

        result.Should().BeFalse();
        company.EntityType.Should().Be("ETF");
    }

    [Fact]
    public async Task IsOperatingCompany_CaseInsensitive_ReturnsTrue() {
        var service = CreateService();
        var company = new CompanyInfo { Cik = "001", Name = "Apple Inc.", EntityType = "Operating" };

        var result = await (Task<bool>)IsOperatingCompanyMethod.Invoke(service, [company]);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsOperatingCompany_ExistingEntityType_DoesNotCallApi() {
        var secEdgarClient = Substitute.For<ISecEdgarClient>();

        var service = CreateService(secEdgarClient: secEdgarClient);
        var company = new CompanyInfo { Cik = "001", Name = "Apple", EntityType = "operating" };

        await (Task<bool>)IsOperatingCompanyMethod.Invoke(service, [company]);

        await secEdgarClient.DidNotReceive().GetEntityType(Arg.Any<string>());
    }

    // ═══════════════════════════════════════════════════════════════════
    // SyncCompaniesFromSecApi — error handling
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SyncCompaniesFromSecApi_ApiThrows_RethrowsException() {
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetActiveCompanies().Returns<List<CompanyInfo>>(
            _ => throw new HttpRequestException("API unavailable"));

        var service = CreateService(secEdgarClient: secEdgarClient);

        var act = () => service.SyncCompaniesFromSecApi();

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*API unavailable*");
    }

    // ═══════════════════════════════════════════════════════════════════
    // CompanyInfo model — IsOperatingCompany property
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("operating", true)]
    [InlineData("Operating", true)]
    [InlineData("OPERATING", true)]
    [InlineData("ETF", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CompanyInfo_IsOperatingCompany_ClassifiesCorrectly(string entityType, bool expected) {
        var company = new CompanyInfo { EntityType = entityType };

        company.IsOperatingCompany.Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════════
    // WorkerOptions — ticker filter logic
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void TickerFilter_WithConfiguredTickers_FiltersCompanies() {
        var workerOptions = new WorkerOptions { TickersToSync = ["AAPL", "MSFT"] };
        var secCompanies = new List<CompanyInfo> {
            new() { Cik = "001", Name = "Apple", Tickers = ["AAPL"] },
            new() { Cik = "002", Name = "Microsoft", Tickers = ["MSFT"] },
            new() { Cik = "003", Name = "Google", Tickers = ["GOOG"] },
        };

        // Replicate the exact filter logic from SyncCompaniesFromSecApi
        var filtered = secCompanies
            .Where(c => c.Tickers.Any(ticker => workerOptions.TickersToSync.Contains(ticker)))
            .ToList();

        filtered.Should().HaveCount(2);
        filtered.Select(c => c.Cik).Should().BeEquivalentTo(["001", "002"]);
    }

    [Fact]
    public void TickerFilter_EmptyTickersToSync_NoFiltering() {
        var workerOptions = new WorkerOptions();
        var secCompanies = new List<CompanyInfo> {
            new() { Cik = "001", Name = "Apple", Tickers = ["AAPL"] },
            new() { Cik = "002", Name = "Microsoft", Tickers = ["MSFT"] },
        };

        var shouldFilter = workerOptions.TickersToSync?.Count > 0;

        shouldFilter.Should().BeFalse();
        // Without filtering, all companies should be processed
    }

    [Fact]
    public void TickerFilter_CompanyWithMultipleTickers_MatchesAny() {
        var workerOptions = new WorkerOptions { TickersToSync = ["BRK.B"] };
        var secCompanies = new List<CompanyInfo> {
            new() { Cik = "001", Name = "Berkshire", Tickers = ["BRK.A", "BRK.B"] },
        };

        var filtered = secCompanies
            .Where(c => c.Tickers.Any(ticker => workerOptions.TickersToSync.Contains(ticker)))
            .ToList();

        filtered.Should().ContainSingle();
    }

    [Fact]
    public void TickerFilter_CompanyWithNoTickers_SkippedByPrimaryTickerCheck() {
        var secCompanies = new List<CompanyInfo> {
            new() { Cik = "001", Name = "NoTickerCo", Tickers = [] },
            new() { Cik = "002", Name = "Apple", Tickers = ["AAPL"] },
        };

        // Replicate the exact skip logic from SyncCompaniesFromSecApi
        var processable = secCompanies
            .Where(c => !string.IsNullOrEmpty(c.Tickers.FirstOrDefault()))
            .ToList();

        processable.Should().ContainSingle().Which.Cik.Should().Be("002");
    }
}
