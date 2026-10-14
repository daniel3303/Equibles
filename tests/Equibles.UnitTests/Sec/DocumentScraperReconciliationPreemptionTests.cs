using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.HostedService;

namespace Equibles.UnitTests.Sec;

// A reconciliation re-sweep can ingest years of filings for one company, so a filing flagged by the
// real-time feed mid-batch must run before the next re-sweep, not after the whole batch.
public class DocumentScraperReconciliationPreemptionTests
{
    private static EquityIssuer Company(string name) => new() { Id = Guid.NewGuid(), Name = name };

    [Fact]
    public async Task ProcessPreemptingReconciliation_FreshFilingMidBatch_RunsBeforeTheNextReSweep()
    {
        var discovered = Company("discovered");
        var first = Company("first re-sweep");
        var second = Company("second re-sweep");
        var fresh = Company("fresh");
        var polls = new Queue<List<EquityIssuer>>([
            [],
            [fresh],
        ]);
        var order = new List<string>();

        await DocumentScraper.ProcessPreemptingReconciliation(
            [discovered],
            [first, second],
            () => Task.FromResult(polls.Count > 0 ? polls.Dequeue() : []),
            company =>
            {
                order.Add(company.Name);
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        order.Should().Equal("discovered", "first re-sweep", "fresh", "second re-sweep");
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
            () => Task.FromResult(new List<EquityIssuer>()),
            company =>
            {
                order.Add(company.Name);
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        order.Should().Equal("discovered and due");
    }
}
