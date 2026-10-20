using Equibles.Holdings.BusinessLogic.Advisers;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Point-in-time Form ADV facts on Postgres: a date reads the roster current then, a no-data
/// month falls back to the roster before it, and a date whose roster the history has not read
/// yet or that precedes the history reads nothing.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class AdviserHistoryPostgresTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    private static readonly DateOnly December = new(2019, 12, 1);
    private static readonly DateOnly January = new(2020, 1, 1);
    private static readonly DateOnly March = new(2020, 3, 1);
    private static readonly DateOnly April = new(2020, 4, 1);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FactsAt_ReadsTheRosterCurrentAtEachDate()
    {
        var holder = new InstitutionalHolder { Cik = "9", Name = "Adviser" };
        await using (var seed = fixture.CreateDbContext())
        {
            seed.AddRange(
                holder,
                new InstitutionalHolderProfile
                {
                    InstitutionalHolderId = holder.Id,
                    EntityType = HolderEntityType.RegisteredAdviser,
                    AdviserCrd = 111,
                },
                Roster(December),
                Roster(January),
                Roster(March),
                // Published but not read by the history yet.
                new FormAdvRosterImport
                {
                    Registration = FormAdvRegistration.Registered,
                    ReportDate = April,
                },
                Version(
                    December,
                    January,
                    1_000,
                    (FormAdvClientType.PooledInvestmentVehicles, 1_000)
                ),
                Version(March, March, 2_000, (FormAdvClientType.Individuals, 2_000)),
                new FormAdvPrivateFundFiling
                {
                    Crd = 111,
                    Registration = FormAdvRegistration.Registered,
                    FilingId = 1,
                    FiledOn = new DateOnly(2019, 3, 28),
                    FundCount = 3,
                    GrossAssets = 1_000,
                    HedgeFundCount = 1,
                    HedgeFundGrossAssets = 900,
                    VentureCapitalFundCount = 2,
                    VentureCapitalFundGrossAssets = 100,
                }
            );
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        var repository = new InstitutionalHolderRepository(db);
        DateOnly[] dates =
        [
            new(2019, 11, 30),
            new(2019, 12, 31),
            new(2020, 2, 29),
            new(2020, 3, 31),
            new(2020, 4, 30),
        ];
        var facts = await AdviserHistory.FactsAt(repository, [111, 999], dates);

        facts.Keys.Select(key => key.Date).Order().Should().Equal(dates[1], dates[2], dates[3]);
        var december = facts[(111, dates[1])];
        december.RosterDate.Should().Be(December);
        december.ClientBase.Should().Be(HolderClientBase.PooledInvestmentVehicles);
        december.RegulatoryAum.Should().Be(1_000);
        december.PrivateFundFocus.Should().Be(PrivateFundFocus.Hedge);
        december.PrivateFundFocusBasis.Should().Be(PrivateFundFocusBasis.GrossAssets);
        facts[(111, dates[2])].RosterDate.Should().Be(January, "February published no data");
        var march = facts[(111, dates[3])];
        march.ClientBase.Should().Be(HolderClientBase.Individuals);
        march.Since.Should().Be(March);

        var timeline = AdviserHistory.Changes(await AdviserHistory.Timeline(repository, 111));
        timeline
            .Select(entry => (entry.Since, entry.Until))
            .Should()
            .Equal((March, March), (December, January));
    }

    private static FormAdvRosterImport Roster(DateOnly reportDate) =>
        new()
        {
            Registration = FormAdvRegistration.Registered,
            ReportDate = reportDate,
            AdviserCount = 1,
            VersionedAt = DateTime.UtcNow,
        };

    private static FormAdvAdviserVersion Version(
        DateOnly since,
        DateOnly until,
        long aum,
        params (FormAdvClientType Type, long Aum)[] clientTypes
    )
    {
        var version = new FormAdvAdviserVersion
        {
            Crd = 111,
            Registration = FormAdvRegistration.Registered,
            ReportDate = since,
            LastSeenReportDate = until,
            ContentHash = since.ToString("O"),
            TotalRegulatoryAum = aum,
            AdvisesPrivateFunds = true,
            PrivateFundCount = 3,
            HedgeFundCount = 1,
            VentureCapitalFundCount = 2,
        };
        version.ClientTypes = clientTypes
            .Select(type => new FormAdvClientTypeAum
            {
                FormAdvAdviserVersionId = version.Id,
                ClientType = type.Type,
                RegulatoryAum = type.Aum,
            })
            .ToList();
        return version;
    }
}
