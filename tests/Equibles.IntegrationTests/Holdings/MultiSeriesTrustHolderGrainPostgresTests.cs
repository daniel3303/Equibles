using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

// A multi-series trust files every fund under one issuer, so a holder-level grouping keyed by
// the issuer alone sums different funds into one row under the primary ticker. On PostgreSQL so
// each grouped projection is proven translatable.
[Collection(ParadeDbCollection.Name)]
public class MultiSeriesTrustHolderGrainPostgresTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private static readonly DateOnly Previous = new(2026, 3, 31);
    private static readonly DateOnly Current = new(2026, 6, 30);

    [Fact]
    public async Task PositionAggregates_KeepEachFundOfTheTrust()
    {
        var (trust, holder) = await SeedTrustBook();

        var positions = await new InstitutionalHoldingRepository(DbContext)
            .Get13FPositionAggregatesByHolder(holder, Current)
            .ToListAsync();

        positions
            .Select(position => (position.CommonStockId, position.ListedTicker, position.Shares))
            .Should()
            .BeEquivalentTo([
                (trust.Id, "IVV", 300L),
                (trust.Id, "EFA", 100L),
                (trust.Id, (string)null, 50L),
            ]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DoubleDown_ReportsTheFundThatGrewUnderItsOwnTicker(bool combined)
    {
        var (trust, _) = await SeedTrustBook();

        var positions = await new InstitutionalHoldingRepository(DbContext)
            .GetDoubleDownPositions(Current, Previous, 50.0, combined)
            .ToListAsync();

        // IVV tripled while EFA halved; summed under the issuer they read +100% as one "AAXJ" row.
        var position = positions.Should().ContainSingle().Which;
        position.CommonStockId.Should().Be(trust.Id);
        position.ListedTicker.Should().Be("IVV");
        position.Ticker.Should().Be("IVV");
        position.PreviousShares.Should().Be(100);
        position.CurrentShares.Should().Be(300);
    }

    [Fact]
    public async Task InstitutionPortfolio_CountsEachFundAsADistinctPosition()
    {
        await SeedTrustBook();

        var output = await new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(DbContext),
            new InstitutionalHolderRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(DbContext),
                new StockSplitRepository(DbContext)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        ).GetInstitutionPortfolio("Trust Holder", reportDate: "2026-06-30");

        output.Should().Contain("Showing holding rows 1-4 of 4");
        output.Should().Contain("(3 distinct tracked stocks");
        output.Should().Contain("1 tracked position(s) have no derivable value");
    }

    // One holder of a trust with a primary listing and two funds. EFA is held as shares and as
    // calls in the current quarter, the call row still awaiting a value.
    private async Task<(EquityIssuer Trust, InstitutionalHolder Holder)> SeedTrustBook()
    {
        EquityIssuer trust = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAXJ",
            Name: "Fund Trust",
            Cik: "1100663",
            ReferenceTickers: ["IVV", "EFA"]
        );
        var holder = new InstitutionalHolder { Cik = "1300", Name = "Trust Holder" };
        DbContext.AddRange(trust, holder);
        DbContext.AddRange(
            Holding(trust, holder, Previous, "IVV", 100),
            Holding(trust, holder, Previous, "EFA", 200),
            Holding(trust, holder, Previous, null, 50),
            Holding(trust, holder, Current, "IVV", 300),
            Holding(trust, holder, Current, "EFA", 60),
            Holding(trust, holder, Current, "EFA", 40, OptionType.Call, valuePending: true),
            Holding(trust, holder, Current, null, 50)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return (trust, holder);
    }

    private static InstitutionalHolding Holding(
        EquityIssuer trust,
        InstitutionalHolder holder,
        DateOnly reportDate,
        string listedTicker,
        long shares,
        OptionType? optionType = null,
        bool valuePending = false
    ) =>
        new()
        {
            EquityIssuerId = trust.Id,
            InstitutionalHolderId = holder.Id,
            ListedTicker = listedTicker,
            FilingDate = reportDate.AddDays(45),
            FilingType = FilingType.Form13F,
            ReportDate = reportDate,
            Shares = shares,
            Value = valuePending ? 0 : shares * 1_000,
            ValuePending = valuePending,
            ShareType = ShareType.Shares,
            OptionType = optionType,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{reportDate:yyyyMMdd}",
        };
}
