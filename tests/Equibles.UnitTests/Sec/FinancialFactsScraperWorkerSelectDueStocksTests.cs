using Equibles.Sec.FinancialFacts.HostedService;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the recheck-window batch selection. The facts walker is the heaviest
/// SEC consumer (one full Company Facts download per tracked company) and its
/// sweep restarts from scratch on every host restart — so the "skip companies
/// checked within the window" filter is what keeps a deploy-heavy day from
/// multiplying the whole universe's downloads by the number of restarts, and
/// the never-checked-first-then-stalest order is what makes an interrupted
/// sweep resume instead of re-walking (mirrors
/// <see cref="DocumentScraperSelectDueCompaniesTests"/>).
/// </summary>
public class FinancialFactsScraperWorkerSelectDueStocksTests
{
    private static readonly DateTime Now = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Cutoff = Now.AddHours(-20);

    [Fact]
    public void NeverCheckedStocksComeFirst()
    {
        var neverChecked = Guid.NewGuid();
        var stale = Guid.NewGuid();

        var due = FinancialFactsScraperWorker.SelectDueStocks(
            [stale, neverChecked],
            new Dictionary<Guid, DateTime> { [stale] = Cutoff.AddHours(-1) },
            Cutoff
        );

        due.Should().HaveCount(2);
        due[0].Should().Be(neverChecked);
        due[1].Should().Be(stale);
    }

    [Fact]
    public void FreshlyCheckedStocksAreNotDue()
    {
        var fresh = Guid.NewGuid();

        var due = FinancialFactsScraperWorker.SelectDueStocks(
            [fresh],
            new Dictionary<Guid, DateTime> { [fresh] = Now.AddHours(-1) },
            Cutoff
        );

        due.Should().BeEmpty();
    }

    [Fact]
    public void StalestStocksAreOrderedFirst()
    {
        var stale3h = Guid.NewGuid();
        var stale9h = Guid.NewGuid();
        var stale6h = Guid.NewGuid();
        var stamps = new Dictionary<Guid, DateTime>
        {
            [stale3h] = Cutoff.AddHours(-3),
            [stale9h] = Cutoff.AddHours(-9),
            [stale6h] = Cutoff.AddHours(-6),
        };

        var due = FinancialFactsScraperWorker.SelectDueStocks(
            [stale3h, stale9h, stale6h],
            stamps,
            Cutoff
        );

        due.Should().Equal(stale9h, stale6h, stale3h);
    }

    [Fact]
    public void StampExactlyAtCutoffIsNotDue()
    {
        // Boundary pin: the filter is strictly-before-cutoff, so a stamp equal
        // to the cutoff counts as fresh — a restart one recheck-window after a
        // check must not re-download that company.
        var atCutoff = Guid.NewGuid();

        var due = FinancialFactsScraperWorker.SelectDueStocks(
            [atCutoff],
            new Dictionary<Guid, DateTime> { [atCutoff] = Cutoff },
            Cutoff
        );

        due.Should().BeEmpty();
    }
}
