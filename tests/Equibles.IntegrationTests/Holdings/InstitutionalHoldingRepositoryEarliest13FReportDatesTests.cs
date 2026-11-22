using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The first-13F test behind the new-filer flag runs on the API's request budget, so it must seek each
/// holder's earliest report date instead of reading the holder's whole filing history.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryEarliest13FReportDatesTests(ParadeDbFixture fixture)
    : IAsyncLifetime
{
    private const int HistoryDays = 3_000;

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetEarliest13FReportDates_ReturnsEachHoldersFirst13FReportDay()
    {
        var (veteran, newcomer, scheduleOnly) = await Seed();
        await using var context = fixture.CreateDbContext();
        var repository = new InstitutionalHoldingRepository(context);

        var dates = await repository
            .GetEarliest13FReportDates([veteran.Id, newcomer.Id, scheduleOnly.Id, Guid.NewGuid()])
            .ToListAsync();

        // A Schedule 13D/G row is no 13F: the veteran's earlier 13G and the schedule-only filer do not count.
        dates
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    new KeyValuePair<Guid, DateOnly>(veteran.Id, ReportDay(0)),
                    new KeyValuePair<Guid, DateOnly>(newcomer.Id, ReportDay(HistoryDays - 1)),
                }
            );
    }

    [Fact]
    public async Task GetEarliest13FReportDates_ReadsOneRowPerHolderNotTheirHistory()
    {
        var (veteran, newcomer, _) = await Seed();
        await using var context = fixture.CreateDbContext();
        var query = new InstitutionalHoldingRepository(context).GetEarliest13FReportDates([
            veteran.Id,
            newcomer.Id,
        ]);

        await context.Database.OpenConnectionAsync();
        await using var command = query.CreateDbCommand();
        command.CommandText = "EXPLAIN (ANALYZE, FORMAT JSON, TIMING OFF) " + command.CommandText;
        var plan = JArray.Parse((string)await command.ExecuteScalarAsync());
        var positionsRead = plan.Descendants()
            .OfType<JObject>()
            .Where(node => (string)node["Relation Name"] == "InstitutionalHolding")
            .Sum(node =>
                ((double)node["Actual Rows"] + ((double?)node["Rows Removed by Filter"] ?? 0))
                * (double)node["Actual Loops"]
            );

        positionsRead
            .Should()
            .BeLessThan(50, "each holder's earliest 13F report date is one index seek");
    }

    private async Task<(
        InstitutionalHolder Veteran,
        InstitutionalHolder Newcomer,
        InstitutionalHolder ScheduleOnly
    )> Seed()
    {
        await using var context = fixture.CreateDbContext();
        EquityIssuer issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "HOLD",
            Name: "Issuer"
        );
        var veteran = new InstitutionalHolder { Cik = "0000000001", Name = "Veteran" };
        var newcomer = new InstitutionalHolder { Cik = "0000000002", Name = "Newcomer" };
        var scheduleOnly = new InstitutionalHolder { Cik = "0000000003", Name = "Schedule only" };
        context.Set<EquityIssuer>().Add(issuer);
        context.Set<InstitutionalHolder>().AddRange(veteran, newcomer, scheduleOnly);
        for (var day = 0; day < HistoryDays; day++)
        {
            context
                .Set<InstitutionalHolding>()
                .Add(NewHolding(issuer, veteran, day, FilingType.Form13F));
        }
        context
            .Set<InstitutionalHolding>()
            .AddRange(
                NewHolding(issuer, veteran, -10, FilingType.Schedule13G),
                NewHolding(issuer, newcomer, HistoryDays - 1, FilingType.Form13F),
                NewHolding(issuer, scheduleOnly, 5, FilingType.Schedule13D)
            );
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("ANALYZE \"InstitutionalHolding\";");
        return (veteran, newcomer, scheduleOnly);
    }

    private static DateOnly ReportDay(int index) => new DateOnly(2000, 1, 1).AddDays(index);

    private static InstitutionalHolding NewHolding(
        EquityIssuer issuer,
        InstitutionalHolder holder,
        int day,
        FilingType filingType
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = issuer.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = ReportDay(day),
            FilingDate = ReportDay(day).AddDays(40),
            FilingType = filingType,
            Shares = 1_000,
            Value = 50_000,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = Guid.NewGuid().ToString()[..20],
        };
}
