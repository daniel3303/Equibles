using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Media.Data;
using Equibles.Sec.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Sec;

public class ChunkRepositoryScopedFallbackQueryTests
{
    [Fact]
    public void BuildScopedFallbackQuery_TranslatesBoundedTickerAndFilingFilters()
    {
        using var context = NewContext();
        var repository = new ChunkRepository(context);
        var documentId = Guid.NewGuid();

        var sql = repository
            .BuildScopedFallbackQuery(
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

    // A document-scoped degrade must narrow on DocumentId ALONE. The MCP SearchDocument tool
    // passes no ticker, so a translation that still demanded one would either throw or, worse,
    // widen the tsvector scan to the whole corpus - the unbounded work this fallback replaces.
    [Fact]
    public void BuildScopedFallbackQuery_DocumentScoped_TranslatesWithoutATickerPredicate()
    {
        using var context = NewContext();
        var repository = new ChunkRepository(context);
        var documentId = Guid.NewGuid();

        var sql = repository
            .BuildScopedFallbackQuery("2026 guidance", 5, documentId: documentId)
            .ToQueryString();

        sql.Should().Contain("to_tsvector");
        sql.Should().Contain("websearch_to_tsquery");
        sql.Should().Contain("@documentId");
        sql.Should().Contain("\"DocumentId\" = @documentId");
        // Ticker is still SELECTed as a column; what must be absent is a ticker PREDICATE.
        sql.Should().NotContain("\"Ticker\" =");
        sql.Should().NotContain("@normalizedTicker");
        sql.Should().Contain("LIMIT");
    }

    // The scope IS the safety argument, so an unscoped call is refused rather than served: a
    // corpus-wide tsvector build is the same unbounded work the BM25 budget already failed on.
    [Fact]
    public void BuildScopedFallbackQuery_RefusesAnUnscopedCall()
    {
        using var context = NewContext();
        var repository = new ChunkRepository(context);

        var refusal = Assert.Throws<ArgumentException>(() =>
            repository.BuildScopedFallbackQuery("2026 guidance", 5)
        );

        refusal.Message.Should().Contain("ticker or a document id");
    }

    private static EquiblesFinancialDbContext NewContext()
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
                new CorporateActionsModuleConfiguration(),
                new MediaModuleConfiguration(),
                new SecModuleConfiguration(),
            }
        );
    }
}
