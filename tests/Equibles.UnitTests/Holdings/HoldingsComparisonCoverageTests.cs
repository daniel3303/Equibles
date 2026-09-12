using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.UnitTests.Holdings;

public class HoldingsComparisonCoverageTests
{
    private static readonly DateOnly Prior = new(2025, 9, 30);
    private static readonly DateOnly Current = new(2025, 12, 31);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_MissingFilersOnEitherSide_WithholdsDelta(bool missingPrior)
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple",
            Cik = "320193",
        };
        var stable = new InstitutionalHolder { Name = "Stable", Cik = "100" };
        var missing = new InstitutionalHolder { Name = "CoverageGapFiler", Cik = "200" };
        db.AddRange(stock, stable, missing);
        db.AddRange(
            Holding(stock, stable, Prior, 100),
            Holding(stock, stable, Current, 110),
            Holding(stock, missing, missingPrior ? Current : Prior, 900)
        );
        await db.SaveChangesAsync();
        var repository = new InstitutionalHoldingRepository(db);

        var coverage = await HoldingsComparisonCoverage.History(
            repository,
            stock,
            [Prior, Current]
        );
        coverage[Current].ComparisonAvailable.Should().BeFalse();
        coverage[Current].CanCompareHolder(stable.Id).Should().BeTrue();
        coverage[Current].CanCompareHolder(missing.Id).Should().BeFalse();
        var history = await Tools(db).GetInstitutionalOwnershipHistory("AAPL");
        history.Should().Contain("unavailable (coverage)");
        var movers = await Tools(db).GetTopInstitutionalBuyersSellers("AAPL", "2025-12-31");
        movers.Should().Contain("Stable").And.NotContain("CoverageGapFiler");
        movers.Should().Contain("no observed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FiledElsewhere_ProvesEntryOrExit_WithoutInferringFromShareCount(bool entry)
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple",
            Cik = "320193",
        };
        var other = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft",
            Cik = "789019",
        };
        var holder = new InstitutionalHolder { Name = "Reporter", Cik = "100" };
        var stable = new InstitutionalHolder { Name = "Stable", Cik = "200" };
        db.AddRange(stock, other, holder, stable);
        db.AddRange(
            Holding(stock, stable, Prior, 100),
            Holding(stock, stable, Current, 100),
            Holding(stock, holder, entry ? Current : Prior, 900),
            Holding(other, holder, entry ? Prior : Current, 0)
        );
        await db.SaveChangesAsync();
        var coverage = await HoldingsComparisonCoverage.History(new(db), stock, [Prior, Current]);
        coverage[Current].ComparisonAvailable.Should().BeTrue();
        var movers = await Tools(db).GetTopInstitutionalBuyersSellers("AAPL", "2025-12-31");
        movers.Should().Contain("Reporter");
    }

    [Fact]
    public async Task Schedule13GDoesNotProvePrior13FFiling()
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple",
            Cik = "320193",
        };
        var holder = new InstitutionalHolder { Name = "Reporter", Cik = "100" };
        db.AddRange(stock, holder);
        var prior = Holding(stock, holder, Prior, 100);
        prior.FilingType = FilingType.Schedule13G;
        db.AddRange(prior, Holding(stock, holder, Current, 900));
        await db.SaveChangesAsync();
        var coverage = await HoldingsComparisonCoverage.History(new(db), stock, [Prior, Current]);
        coverage[Current].MissingPreviousFilers.Should().Contain(holder.Id);
    }

    [Fact]
    public async Task NonconsecutiveQuarters_WithholdComparisonEvenForTheSameFiler()
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple",
            Cik = "320193",
        };
        var holder = new InstitutionalHolder { Name = "Reporter", Cik = "100" };
        db.AddRange(stock, holder);
        var older = new DateOnly(2025, 6, 30);
        db.AddRange(Holding(stock, holder, older, 100), Holding(stock, holder, Current, 110));
        await db.SaveChangesAsync();
        var coverage = await HoldingsComparisonCoverage.History(new(db), stock, [older, Current]);
        coverage[Current].ComparisonAvailable.Should().BeFalse();
        coverage[Current].CanCompareHolder(holder.Id).Should().BeFalse();
    }

    [Fact]
    public async Task UnchangedComparableQuarter_DoesNotClaimThePriorQuarterIsMissing()
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple",
            Cik = "320193",
        };
        var holder = new InstitutionalHolder { Name = "Reporter", Cik = "100" };
        db.AddRange(stock, holder);
        db.AddRange(Holding(stock, holder, Prior, 100), Holding(stock, holder, Current, 100));
        await db.SaveChangesAsync();
        var result = await Tools(db).GetTopInstitutionalBuyersSellers("AAPL", "2025-12-31");
        result
            .Should()
            .Contain("No comparable quarter-over-quarter movement")
            .And.NotContain("No prior quarter")
            .And.NotContain("Comparison unavailable");
    }

    private static EquiblesFinancialDbContext NewDb()
    {
        var db = new EquiblesFinancialDbContext(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .Options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
                new HoldingsModuleConfiguration(),
                new ErrorsModuleConfiguration(),
            }
        );
        db.Database.EnsureCreated();
        return db;
    }

    private static InstitutionalHolding Holding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly date,
        long shares
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = date,
            FilingDate = date.AddDays(45),
            FilingType = FilingType.Form13F,
            Shares = shares,
            Value = shares * 100,
            ShareType = ShareType.Shares,
            AccessionNumber = Guid.NewGuid().ToString("N"),
        };

    private static InstitutionalHoldingsTools Tools(EquiblesFinancialDbContext db) =>
        new(
            new(db),
            new(db),
            new(db),
            new(db),
            new StockCombinedQuarterService(new(db), new(db)),
            new ErrorManager(new ErrorRepository(db)),
            NullLogger<InstitutionalHoldingsTools>.Instance
        );
}
