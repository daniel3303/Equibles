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

namespace Equibles.Tests.Sec;

/// <summary>
/// Tests for <see cref="CompanySyncService"/>.
/// The public SyncCompaniesFromSecApi method depends on PostgreSQL-specific features
/// (List&lt;string&gt; SecondaryTickers). We test the private helper methods via
/// reflection where possible.
/// </summary>
public class CompanySyncServiceTests {
    private static readonly MethodInfo IsOperatingCompanyMethod = typeof(CompanySyncService)
        .GetMethod("IsOperatingCompany", BindingFlags.NonPublic | BindingFlags.Instance);

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
