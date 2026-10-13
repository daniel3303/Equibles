using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Sec.Data;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the recapture cohort's fact check as a first-row lookup. PostgreSQL misjudges the
/// currency-unit filter as rare and plans a NOT EXISTS as a scan of the whole fact table (over
/// two minutes per batch in production); a correlated LIMIT 1 stays a per-document index probe.
/// </summary>
public class XbrlDocumentSetRecaptureQueryTranslationTests
{
    private static EquiblesFinancialDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only", o => o.UseVector())
            .EnableServiceProviderCaching(false)
            .Options;
        return new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new MediaModuleConfiguration(),
                new SecModuleConfiguration(),
                new FinancialFactsModuleConfiguration(),
            }
        );
    }

    [Fact]
    public void Pending_ChecksCurrencyFactsWithAFirstRowLookup_NeverNotExists()
    {
        using var ctx = CreateContext();
        var service = new XbrlDocumentSetRecaptureService(
            new DocumentRepository(ctx),
            new FinancialFactRepository(ctx),
            Substitute.For<ISecEdgarClient>(),
            Substitute.For<IFileManager>(),
            NullLogger<XbrlDocumentSetRecaptureService>.Instance
        );

        var sql = service.Pending().ToQueryString().ToUpperInvariant();

        sql.Should().NotContain("NOT EXISTS");
        sql.Should().Contain("LIMIT 1");
    }
}
