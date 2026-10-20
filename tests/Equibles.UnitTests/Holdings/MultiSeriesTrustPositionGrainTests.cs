using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

// A multi-series trust files every fund under one issuer, so EquityIssuerId alone is not the
// security; ListedTicker (null = the primary listing) is. Each holder-level calculator must keep
// the trust's funds as separate positions instead of summing them under the primary ticker.
public class MultiSeriesTrustPositionGrainTests
{
    private static readonly DateOnly Previous = new(2026, 3, 31);
    private static readonly DateOnly Current = new(2026, 6, 30);

    private static readonly EquityIssuer Trust = Equibles.TestSupport.EquityIssuerSeed.Create(
        Id: Guid.NewGuid(),
        Ticker: "AAXJ",
        Name: "Fund Trust",
        Cik: "C1100663",
        ReferenceTickers: ["IVV", "EFA", "EEM"]
    );

    [Fact]
    public void FundOverlap_KeepsEachFundOfTheTrustAsItsOwnRow()
    {
        var a = Holder("Filer A", "1");
        var b = Holder("Filer B", "2");

        var result = FundOverlapCalculator.Calculate(
            [
                (
                    a,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            Holding(a, "IVV", Current, 1_000, 600_000),
                            Holding(a, "EFA", Current, 2_000, 300_000),
                        ]
                ),
                (
                    b,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            Holding(b, "IVV", Current, 500, 300_000),
                            Holding(b, null, Current, 100, 10_000),
                        ]
                ),
            ],
            Current
        );

        result.Rows.Select(row => row.Ticker).Should().BeEquivalentTo("IVV", "EFA", "AAXJ");
        result.Rows.Select(row => row.ListedTicker).Should().BeEquivalentTo("IVV", "EFA", null);
        result.Rows.Single(row => row.Ticker == "IVV").IsCommon.Should().BeTrue();
        result.Rows.Single(row => row.Ticker == "EFA").IsCommon.Should().BeFalse();
        result.Funds.Select(fund => fund.PositionCount).Should().Equal(2, 2);
        result.UnionPositionCount.Should().Be(3);
        result.IntersectionPositionCount.Should().Be(1);
    }

    [Fact]
    public void PortfolioSummary_CountsFundsAsPositionsAndNeverNetsOneAgainstAnother()
    {
        var holder = Holder("Rotating filer", "3");

        // The filer sold all of one fund and bought the same number of shares of another.
        // Keyed by issuer alone the two trades cancel and turnover reads zero.
        var summary = InstitutionPortfolioSummaryCalculator.Calculate(
            [
                Holding(holder, "EFA", Current, 1_000, 100_000),
                Holding(holder, "EEM", Current, 500, 100_000),
            ],
            [
                Holding(holder, "IVV", Previous, 1_000, 100_000),
                Holding(holder, "EEM", Previous, 500, 100_000),
            ],
            quartersReported: 2,
            Current,
            Previous
        );

        summary.PositionCount.Should().Be(2);
        summary.Top10ConcentrationPercent.Should().Be(100);
        summary.QoQTurnoverPercent.Should().BeGreaterThan(0);
    }

    [Fact]
    public void IndustryAllocation_CountsEachFundAsAPosition()
    {
        var holder = Holder("Allocator", "4");

        var slices = IndustryAllocationCalculator.Calculate([
            Holding(holder, "IVV", Current, 1_000, 100_000),
            Holding(holder, "IVV", Current, 10, 1_000, InvestmentDiscretion.Defined),
            Holding(holder, "EFA", Current, 1_000, 50_000),
            Holding(holder, null, Current, 1_000, 25_000),
        ]);

        slices.Should().ContainSingle().Which.PositionCount.Should().Be(3);
    }

    private static InstitutionalHolder Holder(string name, string cik) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Cik = cik,
        };

    private static InstitutionalHolding Holding(
        InstitutionalHolder holder,
        string listedTicker,
        DateOnly reportDate,
        long shares,
        long value,
        InvestmentDiscretion discretion = InvestmentDiscretion.Sole
    ) =>
        new()
        {
            EquityIssuerId = Trust.Id,
            Issuer = Trust,
            ListedTicker = listedTicker,
            InstitutionalHolderId = holder.Id,
            InstitutionalHolder = holder,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = discretion,
        };
}
