using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The sweep rewrites rows of the table that carries the search index, so its pace is the contract:
/// a bounded number of chunk rows a cycle, one corpus comparison a day.
/// </summary>
public class ChunkTickerConvergencePacerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunCycle_MoreChunksThanTheCycleAllows_LeavesTheRestForLaterCycles()
    {
        var documents = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var restamped = new List<Guid>();
        var scans = 0;
        var pacer = new ChunkTickerConvergencePacer();
        Task<List<Guid>> Find(CancellationToken _)
        {
            scans++;
            return Task.FromResult(documents.ToList());
        }
        Task<int> Restamp(Guid id, CancellationToken _)
        {
            restamped.Add(id);
            return Task.FromResult(ChunkTickerConvergencePacer.MaxChunksPerCycle / 2);
        }

        var first = await pacer.RunCycle(Find, Restamp, Start, CancellationToken.None);
        var second = await pacer.RunCycle(
            Find,
            Restamp,
            Start.AddMinutes(5),
            CancellationToken.None
        );

        first.Documents.Should().Be(2);
        second.Documents.Should().Be(2);
        pacer.Pending.Should().Be(1);
        restamped.Should().Equal(documents.Take(4));
        scans.Should().Be(1);
    }

    [Fact]
    public async Task RunCycle_NothingStale_ComparesTheCorpusAgainOnlyADayLater()
    {
        var scans = 0;
        var pacer = new ChunkTickerConvergencePacer();
        Task<List<Guid>> Find(CancellationToken _)
        {
            scans++;
            return Task.FromResult(new List<Guid>());
        }
        Task<int> Restamp(Guid id, CancellationToken _) => Task.FromResult(0);

        await pacer.RunCycle(Find, Restamp, Start, CancellationToken.None);
        await pacer.RunCycle(Find, Restamp, Start.AddHours(23), CancellationToken.None);
        scans.Should().Be(1);

        await pacer.RunCycle(Find, Restamp, Start.AddHours(24), CancellationToken.None);
        scans.Should().Be(2);
    }

    [Fact]
    public async Task RunCycle_ComparisonFails_IsNotRepeatedOnTheNextCycle()
    {
        var scans = 0;
        var pacer = new ChunkTickerConvergencePacer();
        Task<List<Guid>> Find(CancellationToken _)
        {
            scans++;
            throw new TimeoutException();
        }
        Task<int> Restamp(Guid id, CancellationToken _) => Task.FromResult(0);
        Func<DateTimeOffset, Task> cycle = now =>
            pacer.RunCycle(Find, Restamp, now, CancellationToken.None);

        await cycle.Invoking(run => run(Start)).Should().ThrowAsync<TimeoutException>();
        await cycle(Start.AddMinutes(5));
        scans.Should().Be(1);

        await cycle.Invoking(run => run(Start.AddHours(1))).Should().ThrowAsync<TimeoutException>();
        scans.Should().Be(2);
    }

    [Fact]
    public async Task RunCycle_OneDocumentKeepsFailing_TheOnesBehindItAreStillStamped()
    {
        var failing = Guid.NewGuid();
        var behind = Guid.NewGuid();
        var restamped = new List<Guid>();
        var pacer = new ChunkTickerConvergencePacer();
        Task<List<Guid>> Find(CancellationToken _) =>
            Task.FromResult(new List<Guid> { failing, behind });
        Task<int> Restamp(Guid id, CancellationToken _)
        {
            if (id == failing)
                throw new InvalidOperationException();
            restamped.Add(id);
            return Task.FromResult(1);
        }

        await pacer
            .Invoking(p => p.RunCycle(Find, Restamp, Start, CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidOperationException>();
        await pacer.RunCycle(Find, Restamp, Start.AddMinutes(5), CancellationToken.None);

        restamped.Should().Equal(behind);
    }
}
