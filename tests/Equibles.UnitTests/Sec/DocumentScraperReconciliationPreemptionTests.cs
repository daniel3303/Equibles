using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.HostedService;

namespace Equibles.UnitTests.Sec;

// A reconciliation re-sweep can ingest years of filings for one company, so a filing flagged by the
// real-time feed mid-batch must run before the next re-sweep, not after the whole batch.
public class DocumentScraperReconciliationPreemptionTests
{
    private static EquityIssuer Company(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private static FreshFilingPreemption Recording(
        List<string> order,
        Queue<List<EquityIssuer>> polls,
        params EquityIssuer[] withBacklog
    ) =>
        new(
            () => Task.FromResult(polls.Count > 0 ? polls.Dequeue() : []),
            company =>
            {
                order.Add($"{company.Name} full");
                return Task.CompletedTask;
            },
            company =>
            {
                order.Add($"{company.Name} recent");
                return Task.FromResult(withBacklog.Contains(company));
            },
            CancellationToken.None
        );

    [Fact]
    public async Task ProcessPreemptingReconciliation_FreshFilingMidBatch_RunsBeforeTheNextReSweep()
    {
        var discovered = Company("discovered");
        var first = Company("first re-sweep");
        var second = Company("second re-sweep");
        var fresh = Company("fresh");
        var order = new List<string>();

        await DocumentScraper.ProcessPreemptingReconciliation(
            [discovered],
            [first, second],
            Recording(
                order,
                new Queue<List<EquityIssuer>>([
                    [],
                    [fresh],
                ])
            ),
            CancellationToken.None
        );

        order
            .Should()
            .Equal(
                "discovered recent",
                "first re-sweep full",
                "fresh recent",
                "second re-sweep full"
            );
    }

    // A company the feed already brought in this cycle was just enumerated in full; re-sweeping it is waste.
    [Fact]
    public async Task ProcessPreemptingReconciliation_CompanyAlreadyProcessed_IsNotReSwept()
    {
        var both = Company("discovered and due");
        var order = new List<string>();

        await DocumentScraper.ProcessPreemptingReconciliation(
            [both],
            [both],
            Recording(order, new Queue<List<EquityIssuer>>()),
            CancellationToken.None
        );

        order.Should().Equal("discovered and due recent");
    }

    // Every discovered company's recent filings come before any discovered company's backlog.
    [Fact]
    public async Task ProcessPreemptingReconciliation_DiscoveredCompanyWithABacklog_ReadsItAfterEveryRecentFiling()
    {
        var trust = Company("trust");
        var nvidia = Company("nvidia");
        var due = Company("due");
        var order = new List<string>();

        await DocumentScraper.ProcessPreemptingReconciliation(
            [trust, nvidia],
            [due],
            Recording(order, new Queue<List<EquityIssuer>>(), trust),
            CancellationToken.None
        );

        order.Should().Equal("trust recent", "nvidia recent", "trust full", "due full");
    }
}
