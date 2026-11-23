using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.HostedService;

namespace Equibles.UnitTests.Sec;

// A fund trust's first sweep read 90 reports for over an hour while every new filing on EDGAR waited; running
// whole passes inside one another then starved early-flagged filings behind other trusts' backlogs.
public class FreshFilingPreemptionTests
{
    private static EquityIssuer Company(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private static Func<Task<List<EquityIssuer>>> Polls(params List<EquityIssuer>[] polls)
    {
        var queue = new Queue<List<EquityIssuer>>(polls);
        return () => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : []);
    }

    [Fact]
    public async Task RunFresh_BetweenAFullPassesFilings_ReadsTheFlaggedCompanyBeforeTheNextFiling()
    {
        var trust = Company("trust");
        var fresh = Company("fresh");
        var order = new List<string>();
        FreshFilingPreemption preemption = null;
        preemption = new FreshFilingPreemption(
            Polls([], [fresh]),
            async _ =>
            {
                foreach (var filing in new[] { "report 1", "report 2" })
                {
                    await preemption.RunFresh();
                    order.Add(filing);
                }
            },
            company =>
            {
                order.Add($"{company.Name} recent");
                return Task.FromResult(false);
            },
            CancellationToken.None
        );

        await preemption.Process(trust);

        order.Should().Equal("report 1", "fresh recent", "report 2");
    }

    // A second trust flagged during the first one's sweep reads only its recent filings there; its backlog waits
    // for its own full pass, between companies, and nothing polls inside its fresh pass.
    [Fact]
    public async Task RunFresh_FlaggedCompanyWithABacklog_ReadsOnlyItsRecentFilingsUntilItsOwnFullPass()
    {
        var first = Company("first trust");
        var second = Company("second trust");
        var late = Company("late");
        var polls = new Queue<List<EquityIssuer>>([
            [second],
            [],
            [late],
        ]);
        var pollCount = 0;
        var order = new List<string>();
        FreshFilingPreemption preemption = null;
        preemption = new FreshFilingPreemption(
            () =>
            {
                pollCount++;
                return Task.FromResult(polls.Count > 0 ? polls.Dequeue() : []);
            },
            async company =>
            {
                if (company == second)
                {
                    order.Add("second trust backlog");
                    return;
                }
                foreach (var filing in new[] { "report 1", "report 2" })
                {
                    await preemption.RunFresh();
                    order.Add($"first trust {filing}");
                }
            },
            async company =>
            {
                await preemption.RunFresh();
                order.Add($"{company.Name} recent");
                return company == second;
            },
            CancellationToken.None
        );

        await preemption.Process(first);
        await preemption.DrainFullPasses();

        order
            .Should()
            .Equal(
                "second trust recent",
                "first trust report 1",
                "first trust report 2",
                "late recent",
                "second trust backlog"
            );
        pollCount.Should().Be(3);
        preemption.WasProcessed(second).Should().BeTrue();
    }

    // Its enumeration was read before the flag, so the new filing needs a fresh pass after it, never a nested one.
    [Fact]
    public async Task RunFresh_CompanyFlaggedDuringItsOwnFullPass_GetsAFreshPassAfterIt()
    {
        var trust = Company("trust");
        var passes = new List<string>();
        FreshFilingPreemption preemption = null;
        preemption = new FreshFilingPreemption(
            Polls([trust]),
            async _ =>
            {
                passes.Add("full start");
                await preemption.RunFresh();
                passes.Add("full end");
            },
            _ =>
            {
                passes.Add("fresh");
                return Task.FromResult(false);
            },
            CancellationToken.None
        );

        await preemption.Process(trust);

        passes.Should().Equal("full start", "full end", "fresh");
    }

    // A deploy stopping the worker mid-poll must not skip the rest of the sweep's filings.
    [Fact]
    public async Task RunFresh_ShutdownDuringThePoll_ReturnsWithoutThrowing()
    {
        using var shutdown = new CancellationTokenSource();
        var preemption = new FreshFilingPreemption(
            () =>
            {
                shutdown.Cancel();
                throw new OperationCanceledException(shutdown.Token);
            },
            _ => Task.CompletedTask,
            _ => Task.FromResult(false),
            shutdown.Token
        );

        var poll = () => preemption.RunFresh();

        await poll.Should().NotThrowAsync();
    }
}
