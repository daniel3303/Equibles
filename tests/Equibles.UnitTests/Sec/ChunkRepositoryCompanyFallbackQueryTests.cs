using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Media.Data;
using Equibles.Sec.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Sec;

public class ChunkRepositoryCompanyFallbackQueryTests
{
    [Fact]
    public void BuildCompanyFallbackQuery_TranslatesBoundedTickerAndFilingFilters()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only", o => o.UseVector())
            .EnableServiceProviderCaching(false)
            .Options;
        using var context = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
                new MediaModuleConfiguration(),
                new SecModuleConfiguration(),
            }
        );
        var repository = new ChunkRepository(context);
        var documentId = Guid.NewGuid();

        var sql = repository
            .BuildCompanyFallbackQuery(
                "capital expenditure guidance",
                25,
                "tsm",
                documentId,
                [DocumentType.TenK, DocumentType.TwentyF],
                new DateOnly(2024, 1, 1),
                new DateOnly(2026, 12, 31)
            )
            .ToQueryString();

        sql.Should().Contain("to_tsvector");
        sql.Should().Contain("websearch_to_tsquery");
        sql.Should().Contain("@normalizedTicker='TSM'");
        sql.Should().Contain("\"Ticker\" = @normalizedTicker");
        sql.Should().Contain("@documentId");
        sql.Should().Contain("\"DocumentId\" = @documentId");
        sql.Should().Contain("\"ReportingDate\"");
        sql.Should().Contain("LIMIT");
    }
}
