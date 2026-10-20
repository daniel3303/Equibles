using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.InsiderTrading;

// Record-replay of real Forms 4: each balance a filing reports must count once. A vehicle traded
// several times must not be summed with its own intermediate balances, even when a one-off
// footnote marks one of the trades, and separate vehicles must each count.
public class InsiderPositionBalancesTests
{
    private static List<InsiderTransaction> Parse(string asset)
    {
        var xml = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "InsiderTrading", asset)
        );
        var root = InsiderFilingParser.TryGetOwnershipRoot(xml);
        var period = DateOnly.Parse(root.Element("periodOfReport")!.Value);
        var rows = InsiderFilingParser.ParseTransactions(
            root,
            new InsiderOwner { OwnerCik = "1", Name = "Owner" },
            Guid.NewGuid(),
            new FilingData
            {
                AccessionNumber = "0000000000-26-000001",
                Form = "4",
                FilingDate = period.AddDays(2),
                ReportDate = period,
            },
            false
        );
        return rows.Where(row => row.SecurityKind == InsiderSecurityKind.NonDerivative).ToList();
    }

    private static List<long> Indirect(List<InsiderTransaction> rows) =>
        InsiderPositionBalances
            .Resolve(rows)
            .Rows.Where(row => row.OwnershipNature == OwnershipNature.Indirect)
            .Select(row => row.SharesOwnedAfter)
            .Order()
            .ToList();

    [Fact]
    public void Resolve_NvidiaCeo_CountsEveryNamedVehicle()
    {
        // Accession 0001197649-26-000012: a gift by his trust and nine holding lines, two of them
        // with equal balances under different names.
        var rows = Parse("form4-nvidia-ceo-vehicles.xml");

        var closings = InsiderPositionBalances.Resolve(rows);

        closings.IndirectAmbiguous.Should().BeFalse();
        Indirect(rows).Sum().Should().Be(741_420_494);
        closings
            .Rows.Single(row => row.OwnershipNature == OwnershipNature.Direct)
            .SharesOwnedAfter.Should()
            .Be(70_100_666);
    }

    [Fact]
    public void Resolve_SonidaFundsWithOneOffFootnotes_ChainEachFundsTrades()
    {
        // Accession 0000895345-26-000353: two funds each dispose of shares and then exercise; every
        // line reads "See footnotes" and the first trade of each carries an extra footnote.
        var rows = Parse("form4-sonida-chained-see-footnotes.xml");

        Indirect(rows).Should().Equal(807_115, 6_857_823);
        InsiderPositionBalances.Resolve(rows).IndirectAmbiguous.Should().BeFalse();
    }

    [Fact]
    public void Resolve_FordVotingTrustGifts_KeepOneBalancePerTrustInterest()
    {
        // Accession 0000037996-26-000182: gifts move shares between two voting-trust interests,
        // and the second gift of each carries a footnote the first does not.
        var rows = Parse("form4-ford-voting-trust-gifts.xml");

        Indirect(rows).Should().Equal(15_824, 134_728, 304_177, 1_274_398);
        InsiderPositionBalances.Resolve(rows).IndirectAmbiguous.Should().BeFalse();
    }

    [Fact]
    public void Resolve_ManeFundsSaleThenExercise_ChainEachFundsTrades()
    {
        // Accession 0000899140-26-000947: two funds sell one day and exercise warrants the next;
        // each line's footnote names that trade, not a separate vehicle.
        var rows = Parse("form4-mane-chained-see-footnotes.xml");

        Indirect(rows).Should().Equal(612_033, 3_106_937);
        InsiderPositionBalances.Resolve(rows).IndirectAmbiguous.Should().BeFalse();
    }

    [Fact]
    public void Resolve_DellSeeFootnoteHoldings_CountEachEntity()
    {
        // Accession 0001193125-26-421726: eighteen sales out of one partnership end at zero; four
        // "See footnote" holding lines are four entities.
        var rows = Parse("form4-dell-see-footnote-vehicles.xml");

        Indirect(rows).Should().Equal(0, 277, 305, 51_591, 52_295, 283_752);
        InsiderPositionBalances.Resolve(rows).IndirectAmbiguous.Should().BeFalse();
    }

    [Fact]
    public void Resolve_NelnetHoldingLinesNamedByBalanceFootnotes_CountEachLine()
    {
        // Accession 0001008886-26-000109: a trust's gift leaves 5,000 shares, then holding lines
        // list another "By Trust" at 5,000 and two "By GRAT" at 6,800 each. Each line's balance
        // carries the footnote naming its trust, so equal balances are separate vehicles.
        var rows = Parse("form4-nelnet-balance-footnoted-holdings.xml");

        Indirect(rows).Should().Equal(5_000, 5_000, 6_800, 6_800, 8_200, 10_000, 45_000, 45_192);
    }

    [Fact]
    public void Resolve_SchneiderTrustGiftRepeatedAsAHolding_CountsOnce()
    {
        // Accession 0000892712-26-000043: a gift out of "By Trusts" leaves 1,279,870, and a holding
        // line repeats that balance with no footnote naming another vehicle.
        var rows = Parse("form4-schneider-trust-gift-repeated.xml");

        Indirect(rows).Should().Equal(1_279_870);
        InsiderPositionBalances.Resolve(rows).IndirectAmbiguous.Should().BeFalse();
    }

    [Fact]
    public void Resolve_StrykerTradeWhoseBalanceFootnoteExplainsATransfer_StaysInItsTrust()
    {
        // Accession 0001242914-26-000008: the last "By Revocable Trust" sale starts from a balance
        // no earlier row left, and its balance footnote explains shares transferred in.
        var rows = Parse("form4-stryker-transfer-footnoted-sale.xml");

        Indirect(rows).Should().Equal(37_600, 2_600_815, 12_177_521);
    }

    [Fact]
    public void Resolve_OriginEsppFootnotedBalances_StayOneSpouseBalance()
    {
        // Accession 0001857896-26-000004: "By Spouse" trades whose balance footnotes count ESPP
        // purchases between them remain the spouse's one balance.
        var rows = Parse("form4-origin-espp-footnoted-balances.xml");

        Indirect(rows).Should().Equal(2_798);
    }

    [Fact]
    public void Resolve_LillyCeo_CountsEachTrustAndThe401k()
    {
        // Accession 0001538604-26-000004 attaches its trust footnote to the direct-or-indirect code.
        var rows = Parse("form4-lilly-ceo-vehicles.xml");

        Indirect(rows).Sum().Should().Be(137_065);
    }

    [Fact]
    public void Resolve_RowsNamingNoVehicle_KeepTheNewestBalance()
    {
        // Rows parsed before vehicles were recorded: two sales out of one trust chain, then a line
        // no trade connects, so the balances cannot be told apart.
        var date = new DateOnly(2026, 9, 18);
        var rows = new List<InsiderTransaction>
        {
            Unnamed(0, TransactionCode.Sale, 10_000, 980_531, date),
            Unnamed(1, TransactionCode.Sale, 10_000, 970_531, date),
            Unnamed(2, TransactionCode.Holding, 0, 15_017_750, date),
        };

        var closings = InsiderPositionBalances.Resolve(rows);

        closings.Rows.Single().SharesOwnedAfter.Should().Be(15_017_750);
        closings.IndirectAmbiguous.Should().BeTrue();
        InsiderPositionBalances.Resolve(rows.Take(2)).IndirectAmbiguous.Should().BeFalse();
    }

    private static InsiderTransaction Unnamed(
        int order,
        TransactionCode code,
        long shares,
        long ownedAfter,
        DateOnly date
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            TransactionOrder = order,
            TransactionDate = date,
            TransactionCode = code,
            Shares = shares,
            AcquiredDisposed = AcquiredDisposed.Disposed,
            SharesOwnedAfter = ownedAfter,
            OwnershipNature = OwnershipNature.Indirect,
            SecurityTitle = "Common Stock",
            SecurityKind = InsiderSecurityKind.NonDerivative,
        };
}
