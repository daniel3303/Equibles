using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.HostedService.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins the dated spans the SEC ticker directory leaves behind. A span is evidence that the
/// directory listed the same CIK, ticker and exchange in every capture between its two dates, so a
/// capture that drops the triple must close it and a later return must open a new one; extending
/// the old span across the gap would claim observations that were never made.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class SecTickerDirectoryLedgerTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly Day1 = new(2026, 10, 21);
    private static readonly DateOnly Day2 = new(2026, 10, 22);
    private static readonly DateOnly Day3 = new(2026, 10, 23);

    public SecTickerDirectoryLedgerTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Record_FirstCapture_OpensOneCanonicalSpanPerListedTicker()
    {
        await Record(
            Day1,
            Company("0001730168", ("AVGO", "Nasdaq")),
            Company("0001077428", ("TCBI-PB", "NYSE"))
        );

        var spans = await Spans();
        spans
            .Select(span =>
                (span.Cik, span.Ticker, span.Exchange, span.FirstObservedOn, span.LastObservedOn)
            )
            .Should()
            .BeEquivalentTo([
                ("1730168", "AVGO", "Nasdaq", Day1, Day1),
                ("1077428", "TCBIPB", "NYSE", Day1, Day1),
            ]);
    }

    [Fact]
    public async Task Record_ContinuedListing_ExtendsTheOpenSpan()
    {
        await Record(Day1, Company("1730168", ("AVGO", "Nasdaq")));
        await Record(Day1, Company("1730168", ("AVGO", "Nasdaq")));
        await Record(Day2, Company("1730168", ("AVGO", "Nasdaq")));

        var span = (await Spans()).Should().ContainSingle().Subject;
        span.FirstObservedOn.Should().Be(Day1);
        span.LastObservedOn.Should().Be(Day2);
    }

    [Fact]
    public async Task Record_TickerMissingFromACapture_ClosesItsSpanAndAReturnOpensANewOne()
    {
        await Record(Day1, Company("1", ("GOLD", "NYSE")), Company("2", ("KEEP", "NYSE")));
        await Record(Day2, Company("2", ("KEEP", "NYSE")));
        await Record(Day3, Company("1", ("GOLD", "NYSE")), Company("2", ("KEEP", "NYSE")));

        var gold = (await Spans()).Where(span => span.Ticker == "GOLD").ToList();
        gold.Select(span => (span.FirstObservedOn, span.LastObservedOn))
            .Should()
            .BeEquivalentTo([(Day1, Day1), (Day3, Day3)]);
    }

    [Fact]
    public async Task Record_ExchangeChange_OpensANewSpanAndFreezesTheOldOne()
    {
        await Record(Day1, Company("1", ("UPLS", "OTC")));
        await Record(Day2, Company("1", ("UPLS", "Nasdaq")));

        (await Spans())
            .Select(span => (span.Exchange, span.FirstObservedOn, span.LastObservedOn))
            .Should()
            .BeEquivalentTo([("OTC", Day1, Day1), ("Nasdaq", Day2, Day2)]);
    }

    [Fact]
    public async Task Record_EmptyCapture_RecordsNothingAndClosesNothing()
    {
        await Record(Day1, Company("1730168", ("AVGO", "Nasdaq")));
        (await Record(Day2)).Should().Be(0);
        await Record(Day3, Company("1730168", ("AVGO", "Nasdaq")));

        var span = (await Spans()).Should().ContainSingle().Subject;
        span.LastObservedOn.Should().Be(Day3);
    }

    [Fact]
    public async Task Record_CaptureDatedBeforeTheNewestSpan_IsRefused()
    {
        await Record(Day2, Company("1730168", ("AVGO", "Nasdaq")));

        (await Record(Day1, Company("1", ("NEW", "NYSE")))).Should().Be(0);

        (await Spans()).Should().ContainSingle(span => span.Ticker == "AVGO");
    }

    private async Task<int> Record(DateOnly observedOn, params CompanyInfo[] companies)
    {
        await using var context = Fixture.CreateDbContext();
        return await SecTickerDirectoryLedger.Record(context, companies, observedOn);
    }

    private async Task<List<SecTickerDirectoryObservation>> Spans()
    {
        await using var context = Fixture.CreateDbContext();
        return await context.Set<SecTickerDirectoryObservation>().AsNoTracking().ToListAsync();
    }

    private static CompanyInfo Company(
        string cik,
        params (string Ticker, string Exchange)[] rows
    ) =>
        new()
        {
            Cik = cik,
            Name = "Company " + cik,
            Tickers = rows.Select(row => row.Ticker).ToList(),
            TickerExchanges = rows.ToDictionary(row => row.Ticker, row => row.Exchange),
        };
}
