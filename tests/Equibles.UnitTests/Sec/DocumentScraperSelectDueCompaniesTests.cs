using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.HostedService;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the reconciliation batch selection: never-synced companies (fresh
/// onboarding) always lead, stalest stamps follow, fresh stamps and companies
/// already picked by realtime discovery are excluded, and the per-cycle cap
/// turns a cold start into a rolling backfill instead of one monster cycle.
/// </summary>
public class DocumentScraperSelectDueCompaniesTests
{
    private static readonly DateTime Now = new(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Cutoff = Now.AddHours(-24);

    private static CommonStock Company(string ticker) =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Cik = "1",
        };

    [Fact]
    public void NeverSyncedCompaniesComeFirst()
    {
        var neverSynced = Company("NEW");
        var stale = Company("OLD");

        var due = DocumentScraper.SelectDueCompanies(
            [stale, neverSynced],
            new Dictionary<Guid, DateTime> { [stale.Id] = Cutoff.AddHours(-1) },
            [],
            Cutoff,
            10
        );

        due.Should().HaveCount(2);
        due[0].Should().BeSameAs(neverSynced);
        due[1].Should().BeSameAs(stale);
    }

    [Fact]
    public void FreshlySyncedCompaniesAreNotDue()
    {
        var fresh = Company("FRESH");

        var due = DocumentScraper.SelectDueCompanies(
            [fresh],
            new Dictionary<Guid, DateTime> { [fresh.Id] = Now.AddHours(-1) },
            [],
            Cutoff,
            10
        );

        due.Should().BeEmpty();
    }

    [Fact]
    public void StalestCompaniesAreOrderedFirstAndCapApplies()
    {
        var stale1 = Company("S1");
        var stale2 = Company("S2");
        var stale3 = Company("S3");
        var stamps = new Dictionary<Guid, DateTime>
        {
            [stale1.Id] = Cutoff.AddHours(-3),
            [stale2.Id] = Cutoff.AddHours(-9),
            [stale3.Id] = Cutoff.AddHours(-6),
        };

        var due = DocumentScraper.SelectDueCompanies(
            [stale1, stale2, stale3],
            stamps,
            [],
            Cutoff,
            2
        );

        due.Should().HaveCount(2);
        due[0].Should().BeSameAs(stale2);
        due[1].Should().BeSameAs(stale3);
    }

    [Fact]
    public void CompaniesAlreadySelectedByDiscoveryAreExcluded()
    {
        var dirty = Company("DIRTY");

        var due = DocumentScraper.SelectDueCompanies([dirty], [], [dirty.Id], Cutoff, 10);

        due.Should().BeEmpty();
    }
}
