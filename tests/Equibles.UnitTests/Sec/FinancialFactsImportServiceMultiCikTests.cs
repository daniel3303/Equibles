using System.Net;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.UnitTests.Sec;

// The multi-CIK facts contract (GH-7041): one import reads the primary plus every
// attached secondary CIK, and a fetch failure on ANY of them must abort the cycle
// before a single write — otherwise the checkpoint advances past an unread source
// and a predecessor's older facts are skipped forever.
public class FinancialFactsImportServiceMultiCikTests
{
    [Fact]
    public void CiksFor_PrimaryFirstThenSecondaries()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "XOM",
            Name: "Exxon Mobil",
            Cik: "2115436",
            SecondaryCiks: ["34088", "99999"]
        );

        FinancialFactsImportService.CiksFor(stock).Should().Equal("2115436", "34088", "99999");
    }

    [Fact]
    public void CiksFor_DuplicateSecondary_IsReadOnce()
    {
        // The company sync's subsidiary attach writes SEC's value verbatim, so a
        // duplicate can exist in the column; reading it twice would double the
        // companyfacts download and the in-memory parsed set.
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "XOM",
            Name: "Exxon Mobil",
            Cik: "2115436",
            SecondaryCiks: ["34088", "34088", "2115436"]
        );

        FinancialFactsImportService.CiksFor(stock).Should().Equal("2115436", "34088");
    }

    [Fact]
    public void CiksFor_NoSecondaries_IsJustThePrimary()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple",
            Cik: "320193",
            SecondaryCiks: []
        );

        FinancialFactsImportService.CiksFor(stock).Should().Equal("320193");
    }

    [Fact]
    public async Task Import_SecondaryCikFetchFails_WritesNothing()
    {
        // Primary answers (no data), the attached CIK's download fails: the whole
        // cycle must abort before persistence; reading the retry checkpoint is safe.
        await using var db = new EquiblesFinancialDbContext(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new RetryStatusModule(),
            }
        );
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var provider = Substitute.For<IServiceProvider>();
        provider
            .GetService(typeof(FinancialFactsSyncStatusRepository))
            .Returns(new FinancialFactsSyncStatusRepository(db));
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        scopeFactory.CreateScope().Returns(scope);
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetCompanyFacts("2115436").Returns((CompanyFactsResponse)null);
        secEdgarClient
            .GetCompanyFacts("34088")
            .ThrowsAsync(new HttpRequestException("boom", null, HttpStatusCode.ServiceUnavailable));

        var sut = new FinancialFactsImportService(
            scopeFactory,
            secEdgarClient,
            Substitute.For<ILogger<FinancialFactsImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "XOM",
            Name: "Exxon Mobil",
            Cik: "2115436",
            SecondaryCiks: ["34088"]
        );

        await sut.Import(stock, CancellationToken.None);

        await secEdgarClient.Received(1).GetCompanyFacts("34088");
        (await db.Set<FinancialFactsSyncStatus>().CountAsync()).Should().Be(0);
        db.ChangeTracker.HasChanges().Should().BeFalse();
    }

    private sealed class RetryStatusModule : IModuleConfiguration
    {
        public void ConfigureEntities(ModelBuilder builder) =>
            builder.Entity<FinancialFactsSyncStatus>();
    }
}
