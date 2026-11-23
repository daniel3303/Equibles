using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.HostedService;

namespace Equibles.UnitTests.Sec;

// A fund trust's first sweep read 90 reports for over an hour while every new filing on EDGAR waited,
// because the feed was polled only between companies.
public class FreshFilingPreemptionTests
{
    private static EquityIssuer Company(string name) => new() { Id = Guid.NewGuid(), Name = name };

    [Fact]
    public async Task RunFresh_BetweenOneCompanysFilings_RunsTheFlaggedCompanyBeforeTheNextFiling()
    {
        var trust = Company("trust");
        var fresh = Company("fresh");
        var polls = new Queue<List<EquityIssuer>>([
            [],
            [fresh],
        ]);
        var order = new List<string>();
        FreshFilingPreemption preemption = null;
        preemption = new FreshFilingPreemption(
            () => Task.FromResult(polls.Count > 0 ? polls.Dequeue() : []),
            async company =>
            {
                if (company != trust)
                {
                    order.Add(company.Name);
                    return;
                }
                foreach (var filing in new[] { "report 1", "report 2" })
                {
                    await preemption.RunFresh();
                    order.Add(filing);
                }
            },
            CancellationToken.None
        );

        await preemption.Process(trust);

        order.Should().Equal("report 1", "fresh", "report 2");
    }

    // Its enumeration was read before the flag, so the new filing needs another pass, never a nested one.
    [Fact]
    public async Task RunFresh_CompanyFlaggedWhileInFlight_RunsAgainAfterItsPass()
    {
        var trust = Company("trust");
        var polls = new Queue<List<EquityIssuer>>([
            [trust],
        ]);
        var passes = 0;
        var depth = 0;
        var deepest = 0;
        FreshFilingPreemption preemption = null;
        preemption = new FreshFilingPreemption(
            () => Task.FromResult(polls.Count > 0 ? polls.Dequeue() : []),
            async _ =>
            {
                passes++;
                depth++;
                deepest = Math.Max(deepest, depth);
                await preemption.RunFresh();
                depth--;
            },
            CancellationToken.None
        );

        await preemption.Process(trust);

        passes.Should().Be(2);
        deepest.Should().Be(1);
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
            shutdown.Token
        );

        var poll = () => preemption.RunFresh();

        await poll.Should().NotThrowAsync();
    }

    // A second trust flagged during the first one's sweep keeps polling too, or its own backlog blocks the feed.
    [Fact]
    public async Task RunFresh_FlaggedCompanyWithItsOwnBacklog_StillLetsLaterFilingsPreemptIt()
    {
        var first = Company("first trust");
        var second = Company("second trust");
        var fresh = Company("fresh");
        var polls = new Queue<List<EquityIssuer>>([
            [second],
            [],
            [fresh],
        ]);
        var order = new List<string>();
        FreshFilingPreemption preemption = null;
        preemption = new FreshFilingPreemption(
            () => Task.FromResult(polls.Count > 0 ? polls.Dequeue() : []),
            async company =>
            {
                if (company == fresh)
                {
                    order.Add(company.Name);
                    return;
                }
                foreach (var filing in new[] { "report 1", "report 2" })
                {
                    await preemption.RunFresh();
                    order.Add($"{company.Name} {filing}");
                }
            },
            CancellationToken.None
        );

        await preemption.Process(first);

        order
            .Should()
            .Equal(
                "second trust report 1",
                "fresh",
                "second trust report 2",
                "first trust report 1",
                "first trust report 2"
            );
    }
}
