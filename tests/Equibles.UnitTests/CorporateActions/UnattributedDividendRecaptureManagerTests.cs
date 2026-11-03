using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.CorporateActions;

// Issuer-only dividends from captures before listing attribution are invisible to every
// listing-keyed read; the pass re-reads them from the source so attributed rows exist.
public class UnattributedDividendRecaptureManagerTests
{
    private static IModuleConfiguration[] Modules =>
        [new CommonStocksModuleConfiguration(), new CorporateActionsModuleConfiguration()];

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new EquiblesFinancialDbContext(options, Modules);
        db.Database.EnsureCreated();
        return db;
    }

    private static UnattributedDividendRecaptureManager NewManager(
        EquiblesFinancialDbContext db,
        IYahooFinanceClient yahoo
    )
    {
        var dividends = new CashDividendRepository(db);
        var issuers = new EquityIssuerRepository(db);
        return new UnattributedDividendRecaptureManager(
            dividends,
            issuers,
            new CashDividendBackfillManager(
                yahoo,
                new CashDividendCaptureManager(dividends, issuers)
            ),
            NullLogger<UnattributedDividendRecaptureManager>.Instance
        );
    }

    private static async Task<EquityIssuer> AddIssuer(EquiblesFinancialDbContext db, string ticker)
    {
        EquityIssuer issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker,
            Name: $"{ticker} Inc."
        );
        issuer.Presentation.Listing.TradingCurrency = "USD";
        db.Add(issuer);
        await db.SaveChangesAsync();
        return issuer;
    }

    private static CashDividend Dividend(EquityIssuer issuer, DateOnly exDate, bool attributed) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            EquityListingId = attributed ? issuer.Presentation.EquityListingId : null,
            Currency = attributed ? "USD" : null,
            ExDate = exDate,
            AmountPerShare = 0.25m,
            Source = CashDividendSource.Yahoo,
        };

    private static IYahooFinanceClient YahooServing(string symbol, params DateOnly[] exDates)
    {
        var client = Substitute.For<IYahooFinanceClient>();
        client
            .GetChart(symbol, Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(
                new YahooChartData
                {
                    Dividends = exDates
                        .Select(date => new CashDividendEvent { Date = date, Amount = 0.25m })
                        .ToList(),
                    SourceIdentity = new()
                    {
                        Symbol = symbol,
                        Currency = "USD",
                        ExchangeCode = "NYQ",
                        ExchangeTimeZone = "America/New_York",
                    },
                }
            );
        return client;
    }

    [Fact]
    public async Task RecaptureNext_ReReadsAnIssuerWhoseDividendsAreIssuerOnly()
    {
        await using var db = NewDb();
        var issuer = await AddIssuer(db, "AESI");
        db.AddRange(
            Dividend(issuer, new DateOnly(2025, 5, 15), attributed: false),
            Dividend(issuer, new DateOnly(2025, 8, 14), attributed: false)
        );
        await db.SaveChangesAsync();
        var yahoo = YahooServing("AESI", new DateOnly(2025, 5, 15), new DateOnly(2025, 8, 14));

        var attempted = await NewManager(db, yahoo).RecaptureNext([], 10, CancellationToken.None);

        attempted.Should().Equal(issuer.Id);
        await yahoo.Received(1).GetChart("AESI", new DateOnly(2025, 5, 12), Arg.Any<DateOnly>());
        var listingRows = await new CashDividendRepository(db)
            .GetHistoryByListing(issuer.Presentation.EquityListingId)
            .Select(row => row.ExDate)
            .ToListAsync();
        listingRows.Should().Equal(new DateOnly(2025, 8, 14), new DateOnly(2025, 5, 15));
        (await new CashDividendRepository(db).GetUnattributedWithoutListingRow(3).AnyAsync())
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task RecaptureNext_SkipsCoveredIssuersAttemptedOnesAndRetiredPresentations()
    {
        await using var db = NewDb();
        var covered = await AddIssuer(db, "COVR");
        var attempted = await AddIssuer(db, "TRYD");
        var retired = await AddIssuer(db, "GONE");
        retired.Presentation.Listing.Active = false;
        db.AddRange(
            // A provider dated the same payment a day apart: the attributed row serves it.
            Dividend(covered, new DateOnly(2025, 3, 3), attributed: false),
            Dividend(covered, new DateOnly(2025, 3, 4), attributed: true),
            Dividend(attempted, new DateOnly(2025, 3, 3), attributed: false),
            Dividend(retired, new DateOnly(2025, 3, 3), attributed: false)
        );
        await db.SaveChangesAsync();
        var yahoo = Substitute.For<IYahooFinanceClient>();

        var result = await NewManager(db, yahoo)
            .RecaptureNext([attempted.Id], 10, CancellationToken.None);

        result.Should().BeEmpty();
        await yahoo.DidNotReceiveWithAnyArgs().GetChart(default, default, default);
    }

    [Fact]
    public async Task RecaptureNext_ASourceFailureCountsAsAttemptedAndKeepsTheRows()
    {
        await using var db = NewDb();
        var issuer = await AddIssuer(db, "FAIL");
        db.Add(Dividend(issuer, new DateOnly(2025, 3, 3), attributed: false));
        await db.SaveChangesAsync();
        var yahoo = Substitute.For<IYahooFinanceClient>();
        yahoo
            .GetChart(default, default, default)
            .ReturnsForAnyArgs<YahooChartData>(_ => throw new HttpRequestException("404"));

        var attempted = await NewManager(db, yahoo).RecaptureNext([], 10, CancellationToken.None);

        attempted.Should().Equal(issuer.Id);
        (await new CashDividendRepository(db).GetUnattributedWithoutListingRow(3).CountAsync())
            .Should()
            .Be(1);
    }

    [Fact]
    public void GetUnattributedWithoutListingRow_TranslatesOnNpgsql()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only")
            .Options;
        using var db = new EquiblesFinancialDbContext(options, Modules);

        var sql = new CashDividendRepository(db)
            .GetUnattributedWithoutListingRow(3)
            .Where(row => row.Issuer.Presentation.Listing.Active)
            .GroupBy(row => row.EquityIssuerId)
            .Select(group => new { group.Key, Oldest = group.Min(row => row.ExDate) })
            .OrderBy(row => row.Key)
            .Take(100)
            .ToQueryString();

        sql.Should().Contain("NOT EXISTS").And.Contain("GROUP BY").And.Contain("LIMIT");
    }
}
