using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class FundOverlapCalculatorIsCommonPartialOverlapTests
{
    private static readonly DateOnly Report = new(2024, 12, 31);

    // The existing TwoIdenticalFunds pin asserts every row's IsCommon = true
    // when both funds hold the same set of stocks. The partial-overlap arm —
    // a stock held by ONE fund must carry IsCommon = false while the SHARED
    // stock keeps IsCommon = true — is unpinned. A refactor that hard-coded
    // IsCommon = true (or tied it to a different signal like
    // `CombinedValue > 0`) would pass the all-identical pin yet mislabel
    // every single-fund row as "consensus", breaking the consensus-only
    // filters in the consumer views and the JaccardSimilarity ratio that
    // derives IntersectionPositionCount from `allHaveIt`.
    [Fact]
    public void Calculate_PartialOverlap_IsCommonTrueOnSharedRowAndFalseOnSingleFundRows()
    {
        EquityIssuer aapl = MakeStock("AAPL", "Apple Inc.");
        EquityIssuer msft = MakeStock("MSFT", "Microsoft Corp.");
        EquityIssuer nvda = MakeStock("NVDA", "NVIDIA Corp.");
        var fundA = MakeHolder("Fund A", "C001");
        var fundB = MakeHolder("Fund B", "C002");

        var result = FundOverlapCalculator.Calculate(
            [
                (
                    fundA,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(fundA, aapl, shares: 1_000, value: 1_000_000),
                            MakeHolding(fundA, msft, shares: 500, value: 500_000),
                        ]
                ),
                (
                    fundB,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(fundB, aapl, shares: 800, value: 800_000),
                            MakeHolding(fundB, nvda, shares: 300, value: 300_000),
                        ]
                ),
            ],
            Report
        );

        var aaplRow = result.Rows.Single(r => r.Ticker == "AAPL");
        var msftRow = result.Rows.Single(r => r.Ticker == "MSFT");
        var nvdaRow = result.Rows.Single(r => r.Ticker == "NVDA");

        aaplRow.IsCommon.Should().BeTrue();
        msftRow.IsCommon.Should().BeFalse();
        nvdaRow.IsCommon.Should().BeFalse();
    }

    // A reported position whose value is pending or unavailable is still held: the consensus
    // count reads presence, so two funds reporting XYZ count two even when one row is at $0.
    [Fact]
    public void Calculate_PositionValuedAtZero_StillCountsItsFundAsHolding()
    {
        EquityIssuer xyz = MakeStock("XYZ", "XYZ Corp.");
        EquityIssuer aapl = MakeStock("AAPL", "Apple Inc.");
        var fundA = MakeHolder("Fund A", "C001");
        var fundB = MakeHolder("Fund B", "C002");

        var result = FundOverlapCalculator.Calculate(
            [
                (
                    fundA,
                    (IReadOnlyList<InstitutionalHolding>)
                        [MakeHolding(fundA, xyz, shares: 1_000, value: 50_000)]
                ),
                (
                    fundB,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(fundB, xyz, shares: 400, value: 0),
                            MakeHolding(fundB, aapl, shares: 10, value: 2_000),
                        ]
                ),
            ],
            Report
        );

        result.Rows.Single(r => r.Ticker == "XYZ").HeldByCount.Should().Be(2);
        result.Rows.Single(r => r.Ticker == "AAPL").HeldByCount.Should().Be(1);
    }

    // A row with zero shares and $0 is a position the filer left during the quarter, not a
    // holding: it counts toward no consensus, intersection or position count, and a security
    // only such rows name has no row at all.
    [Fact]
    public void Calculate_PositionReportingNoSharesAndNoValue_IsNotAHolding()
    {
        EquityIssuer xyz = MakeStock("XYZ", "XYZ Corp.");
        EquityIssuer msft = MakeStock("MSFT", "Microsoft Corp.");
        EquityIssuer aapl = MakeStock("AAPL", "Apple Inc.");
        var fundA = MakeHolder("Fund A", "C001");
        var fundB = MakeHolder("Fund B", "C002");

        var result = FundOverlapCalculator.Calculate(
            [
                (
                    fundA,
                    (IReadOnlyList<InstitutionalHolding>)
                        [MakeHolding(fundA, xyz, shares: 1_000, value: 50_000)]
                ),
                (
                    fundB,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(fundB, xyz, shares: 0, value: 0),
                            MakeHolding(fundB, msft, shares: 0, value: 0),
                            MakeHolding(fundB, aapl, shares: 10, value: 2_000),
                        ]
                ),
            ],
            Report
        );

        var xyzRow = result.Rows.Single(r => r.Ticker == "XYZ");
        xyzRow.HeldByCount.Should().Be(1);
        xyzRow.IsCommon.Should().BeFalse();
        result.Rows.Should().NotContain(r => r.Ticker == "MSFT");
        result.IntersectionPositionCount.Should().Be(0);
        result.Funds.Single(f => f.HolderId == fundB.Id).PositionCount.Should().Be(1);
    }

    // The pairwise matrix reads the same presence: a zero-valued held position is shared, a
    // zero-share $0 row is not.
    [Fact]
    public void ComputePairwiseOverlap_ReadsTheSamePresenceAsConsensus()
    {
        EquityIssuer xyz = MakeStock("XYZ", "XYZ Corp.");
        EquityIssuer msft = MakeStock("MSFT", "Microsoft Corp.");
        var fundA = MakeHolder("Fund A", "C001");
        var fundB = MakeHolder("Fund B", "C002");

        var overlap = FundOverlapCalculator.Calculate(
            [
                (
                    fundA,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(fundA, xyz, shares: 1_000, value: 50_000),
                            MakeHolding(fundA, msft, shares: 500, value: 25_000),
                        ]
                ),
                (
                    fundB,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(fundB, xyz, shares: 400, value: 0),
                            MakeHolding(fundB, msft, shares: 0, value: 0),
                        ]
                ),
            ],
            Report
        );

        var matrix = FundOverlapCalculator.ComputePairwiseOverlap(overlap);

        matrix.SharedTickerCounts[0][1].Should().Be(1);
        matrix.SharedTickerCounts[1][1].Should().Be(1);
        overlap.IntersectionPositionCount.Should().Be(1);
    }

    // A fund holds a stock through its common shares: a fund holding only puts on it, or only
    // its notes, does not hold it, and a fund holding shares beside a call and notes shows its
    // shares alone, weighted against its common-share total.
    [Fact]
    public void Calculate_CountsCommonShareRowsOnly()
    {
        EquityIssuer xyz = MakeStock("XYZ", "XYZ Corp.");
        EquityIssuer msft = MakeStock("MSFT", "Microsoft Corp.");
        var fundA = MakeHolder("Fund A", "C001");
        var fundB = MakeHolder("Fund B", "C002");
        var fundC = MakeHolder("Fund C", "C003");

        var result = FundOverlapCalculator.Calculate(
            [
                (
                    fundA,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(fundA, xyz, shares: 1_000, value: 50_000),
                            MakeHolding(fundA, msft, shares: 10, value: 1_000),
                        ]
                ),
                (
                    fundB,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(
                                fundB,
                                xyz,
                                shares: 5_000,
                                value: 250_000,
                                optionType: OptionType.Put
                            ),
                            MakeHolding(fundB, msft, shares: 100, value: 10_000),
                            MakeHolding(
                                fundB,
                                msft,
                                shares: 900,
                                value: 90_000,
                                optionType: OptionType.Call
                            ),
                            MakeHolding(
                                fundB,
                                msft,
                                shares: 7_000,
                                value: 7_000,
                                shareType: ShareType.Principal
                            ),
                        ]
                ),
                (
                    fundC,
                    (IReadOnlyList<InstitutionalHolding>)
                        [
                            MakeHolding(
                                fundC,
                                xyz,
                                shares: 20_000,
                                value: 20_000,
                                shareType: ShareType.Principal
                            ),
                            MakeHolding(fundC, msft, shares: 30, value: 3_000),
                        ]
                ),
            ],
            Report
        );

        var xyzRow = result.Rows.Single(r => r.Ticker == "XYZ");
        xyzRow.HeldByCount.Should().Be(1, "only Fund A holds XYZ's shares");
        xyzRow.IsCommon.Should().BeFalse();
        xyzRow
            .CombinedValue.Should()
            .Be(50_000, "a put's notional and a note's amount add no value");

        var msftRow = result.Rows.Single(r => r.Ticker == "MSFT");
        msftRow.IsCommon.Should().BeTrue();
        var fundBSlice = msftRow.Slices.Single(s => s.HolderId == fundB.Id);
        fundBSlice.Shares.Should().Be(100);
        fundBSlice.Value.Should().Be(10_000);
        fundBSlice
            .PercentOfPortfolio.Should()
            .BeApproximately(100.0, 1e-9, "its common-share total is MSFT's $10,000");
        result.IntersectionPositionCount.Should().Be(1);
    }

    private static EquityIssuer MakeStock(string ticker, string name) =>
        Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker,
            Name: name,
            Cik: "C" + Guid.NewGuid().ToString("N")[..7]
        );

    private static InstitutionalHolder MakeHolder(string name, string cik) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Cik = cik,
        };

    private static InstitutionalHolding MakeHolding(
        InstitutionalHolder holder,
        EquityIssuer stock,
        long shares,
        long value,
        OptionType? optionType = null,
        ShareType shareType = ShareType.Shares
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            Issuer = Equibles.TestSupport.NativeListingSeed.ForStock(null, stock).Security.Issuer,
            InstitutionalHolderId = holder.Id,
            InstitutionalHolder = holder,
            FilingDate = Report.AddDays(45),
            ReportDate = Report,
            Shares = shares,
            Value = value,
            ShareType = shareType,
            OptionType = optionType,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
        };
}
