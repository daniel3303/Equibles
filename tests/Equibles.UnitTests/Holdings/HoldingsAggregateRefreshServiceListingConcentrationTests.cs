using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// The in-memory branch of the per-listing concentration pass must produce the same figures
/// the SQL branch stores: filers counted once, their rows summed before squaring and ranking,
/// a legacy null label folded into the presentation ticker, and only the quarter's own rows.
/// </summary>
public class HoldingsAggregateRefreshServiceListingConcentrationTests
{
    private static readonly DateOnly QPrev = new(2024, 9, 30);
    private static readonly DateOnly QCur = new(2024, 12, 31);

    [Fact]
    public async Task LoadListingConcentration_InMemory_RanksEachListingsFilerSums()
    {
        await using var db = new EquiblesFinancialDbContext(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new HoldingsModuleConfiguration(),
            }
        );
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "TRST", Name: "Trust");
        var holders = Enumerable
            .Range(1, 12)
            .Select(i => new InstitutionalHolder { Cik = $"{i}", Name = $"Holder {i}" })
            .ToList();
        db.AddRange(
            Row(holders[0], QCur, 50_000),
            Row(holders[1], QCur, 40_000, listedTicker: "TRST"),
            Row(holders[0], QCur, 300, listedTicker: "TRST.B"),
            Row(holders[0], QCur, 200, listedTicker: "TRST.B", optionType: OptionType.Put),
            Row(holders[1], QCur, 1_000, listedTicker: "TRST.B"),
            Row(holders[2], QPrev, 250, listedTicker: "TRST.B"),
            Row(holders[11], QCur, 0, listedTicker: "TRST.B")
        );
        for (var i = 2; i < 11; i++)
            db.Add(Row(holders[i], QCur, 10 * (i + 1), listedTicker: "TRST.B"));
        await db.SaveChangesAsync();

        var rows = await HoldingsAggregateRefreshService.LoadListingConcentration(
            db,
            QCur,
            CancellationToken.None
        );

        rows.Should().HaveCount(2);
        var primary = rows.Single(row => row.PriceSeriesTicker == "TRST");
        primary.HolderCount.Should().Be(2);
        primary.TotalValue.Should().Be(90_000);
        primary.TopOneValue.Should().Be(50_000);
        primary.TopFiveValue.Should().Be(90_000);
        primary.ValueSquaredSum.Should().Be(50_000d * 50_000 + 40_000d * 40_000);

        var sibling = rows.Single(row => row.PriceSeriesTicker == "TRST.B");
        var expected = new List<long> { 500, 1_000, 0 }
            .Concat(Enumerable.Range(2, 9).Select(i => 10L * (i + 1)))
            .OrderByDescending(value => value)
            .ToList();
        sibling.HolderCount.Should().Be(expected.Count);
        sibling.TotalValue.Should().Be(expected.Sum());
        sibling.ValueSquaredSum.Should().Be(expected.Sum(value => (double)value * value));
        sibling.TopOneValue.Should().Be(1_000);
        sibling.TopFiveValue.Should().Be(expected.Take(5).Sum());
        sibling.TopTenValue.Should().Be(expected.Take(10).Sum());

        InstitutionalHolding Row(
            InstitutionalHolder holder,
            DateOnly date,
            long value,
            string listedTicker = null,
            OptionType? optionType = null
        ) =>
            new()
            {
                Issuer = stock,
                InstitutionalHolder = holder,
                ListedTicker = listedTicker,
                OptionType = optionType,
                ReportDate = date,
                FilingDate = date.AddDays(40),
                FilingType = FilingType.Form13F,
                Shares = value / 10,
                Value = value,
                AccessionNumber = $"{holder.Cik}-{date:yyyyMMdd}-{listedTicker}-{value}",
            };
    }
}
