using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// The swapped-column signal is the whole filing fitting the quarter price only with its share and
// value columns exchanged; no single row, and no filing a unit or duplicate reading explains.
public class Swapped13FColumnRepairerTests
{
    private static readonly DateOnly ThousandsEraFilingDate = new(2022, 2, 14);
    private static readonly DateOnly WholeDollarFilingDate = new(2023, 5, 15);
    private static readonly decimal[] SpreadPrices = [12.5m, 48m, 96.2m, 150m, 310.75m, 4.1m];

    [Fact]
    public void IsSuspect_EveryPricedRowSwapped_True()
    {
        var rows = SpreadPrices
            .Select((price, i) => Swapped(1_000 * (i + 1), price, WholeDollarFilingDate))
            .ToList();

        Swapped13FColumnRepairer.IsSuspect(rows, PriceOf).Should().BeTrue();
    }

    [Fact]
    public void IsSuspect_DuplicatedValueColumn_False()
    {
        // The duplicated-column defect writes the value into both columns; swapping them changes
        // nothing, so it reads as the price only for a $1 stock.
        var rows = SpreadPrices
            .Select(price => Row(25_000, 25_000, price, WholeDollarFilingDate))
            .ToList();

        Swapped13FColumnRepairer.IsSuspect(rows, PriceOf).Should().BeFalse();
    }

    [Fact]
    public void IsSuspect_CorrectThousandsFilingNearTheUnitsSquareRoot_False()
    {
        // At about $31.62 a correct thousands-era row fits both readings, so it is no evidence.
        var rows = Enumerable
            .Range(1, 6)
            .Select(i => AsFiled(10_000 * i, 31.6m, ThousandsEraFilingDate))
            .ToList();

        Swapped13FColumnRepairer.IsSuspect(rows, PriceOf).Should().BeFalse();
    }

    [Fact]
    public void IsSuspect_WholeDollarFilerStillReportingThousands_False()
    {
        // n shares and n x P / 1000 as value: the swap fits near $31.62, but so does the filing
        // as filed once its value is read in thousands.
        var rows = Enumerable
            .Range(1, 6)
            .Select(i =>
            {
                var shares = 100_000L * i;
                return Row(shares, (long)(shares * 31.6m / 1000), 31.6m, WholeDollarFilingDate);
            })
            .ToList();

        Swapped13FColumnRepairer.IsSuspect(rows, PriceOf).Should().BeFalse();
    }

    [Fact]
    public void IsSuspect_FewerThanFivePricedRows_False()
    {
        var rows = SpreadPrices
            .Take(4)
            .Select(price => Swapped(5_000, price, WholeDollarFilingDate))
            .Append(Swapped(5_000, null, WholeDollarFilingDate))
            .ToList();

        Swapped13FColumnRepairer.IsSuspect(rows, PriceOf).Should().BeFalse();
    }

    [Fact]
    public void IsSuspect_SwappedRowsBelowEightyPercent_False()
    {
        var swapped = SpreadPrices
            .Take(3)
            .Select(price => Swapped(5_000, price, WholeDollarFilingDate));
        var asFiled = SpreadPrices
            .Skip(3)
            .Select(price => AsFiled(5_000, price, WholeDollarFilingDate));

        Swapped13FColumnRepairer
            .IsSuspect(swapped.Concat(asFiled).ToList(), PriceOf)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void IsSuspect_OptionRowsAreNoEvidence()
    {
        // An option row's value is the contract's, not the underlying shares'.
        var rows = SpreadPrices
            .Select(price => Swapped(5_000, price, WholeDollarFilingDate, OptionType.Call))
            .ToList();

        Swapped13FColumnRepairer.IsSuspect(rows, PriceOf).Should().BeFalse();
    }

    [Fact]
    public void Repair_SwapsEveryShareRowButOneThatFitsAsFiled()
    {
        var swapped = SpreadPrices
            .Select(price => Swapped(2_000, price, ThousandsEraFilingDate))
            .ToList();
        var asFiled = AsFiled(4_000, 77m, ThousandsEraFilingDate);
        var unpriced = Swapped(3_000, null, ThousandsEraFilingDate);
        var option = Swapped(1_000, 20m, ThousandsEraFilingDate, OptionType.Put);
        var principal = Row(
            500_000,
            498,
            null,
            ThousandsEraFilingDate,
            shareType: ShareType.Principal
        );
        var rows = swapped.Concat([asFiled, unpriced, option, principal]).ToList();
        var revalued = new List<BufferedHoldingRow>();

        Swapped13FColumnRepairer.IsSuspect(rows, PriceOf).Should().BeTrue();
        var repaired = Swapped13FColumnRepairer.Repair(rows, PriceOf, revalued.Add);

        repaired.Should().Be(SpreadPrices.Length + 2);
        revalued.Should().HaveCount(repaired).And.NotContain([asFiled, principal]);
        swapped.Should().OnlyContain(row => row.Holding.Shares == 2_000);
        unpriced.Holding.Shares.Should().Be(3_000);
        unpriced.ManagerEntry.Shares.Should().Be(3_000);
        // The share column held whole dollars even in the thousands era.
        unpriced.Holding.FiledValue.Should().Be(3_000 * 40);
        option.Holding.Shares.Should().Be(1_000);
        asFiled.Holding.Shares.Should().Be(4_000);
        principal.Holding.Shares.Should().Be(500_000);
    }

    private decimal? PriceOf(InstitutionalHolding holding) =>
        Prices.TryGetValue(holding.EquityIssuerId, out var price) ? price : null;

    private readonly Dictionary<Guid, decimal> Prices = [];

    // n shares worth n x P, filed with the dollars in the share column and n in the value column.
    // An unpriced row is filed as if it traded at $40.
    private BufferedHoldingRow Swapped(
        long shares,
        decimal? price,
        DateOnly filingDate,
        OptionType? optionType = null
    ) => Row((long)(shares * (price ?? 40m)), shares, price, filingDate, optionType: optionType);

    private BufferedHoldingRow AsFiled(long shares, decimal price, DateOnly filingDate)
    {
        var dollars = shares * price;
        var reported =
            filingDate < FiledValueScale.WholeDollarEffectiveDate ? dollars / 1000 : dollars;
        return Row(shares, (long)reported, price, filingDate);
    }

    private BufferedHoldingRow Row(
        long shares,
        long reportedValue,
        decimal? price,
        DateOnly filingDate,
        OptionType? optionType = null,
        ShareType shareType = ShareType.Shares
    )
    {
        var issuer = Guid.NewGuid();
        if (price.HasValue)
            Prices[issuer] = price.Value;
        return new BufferedHoldingRow
        {
            Holding = new InstitutionalHolding
            {
                EquityIssuerId = issuer,
                FilingDate = filingDate,
                ReportDate = new DateOnly(2021, 12, 31),
                Shares = shares,
                FiledValue = (long)FiledValueScale.ToDollars(reportedValue, filingDate),
                ShareType = shareType,
                OptionType = optionType,
            },
            ManagerEntry = new HoldingManagerEntry { Shares = shares },
            ReportedValue = reportedValue,
        };
    }
}
