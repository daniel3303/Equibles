using Equibles.Holdings.HostedService.Models;
using Equibles.Integrations.Sec.Models;
using static Equibles.Holdings.HostedService.Services.Realtime13FIngestionService;

namespace Equibles.UnitTests.Holdings;

public class RecoveryTailWalkTests
{
    private static readonly EntryImport Stuck = new(EntryImportOutcome.Incomplete, true);
    private static readonly EntryImport Imported = new(EntryImportOutcome.Imported, false);

    [Fact]
    public async Task StuckPendingFiling_SkipsTheStoredTail_AndResolvesNothing()
    {
        var walk = new Walk(("stuck", Stuck), ("stored-1", Imported), ("stored-2", Imported));

        var result = await walk.Run(pending: ["stuck"], processed: ["stored-1", "stored-2"]);

        walk.Attempted.Should()
            .Equal(["stuck"], "nothing changed holdings, so the stored tail stays as it is");
        result.Skipped.Should().Be(2);
        result.Resolvable.Should().BeEmpty();
        result.Complete.Should().BeFalse();
        walk.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task LaterPendingFilingThatImports_ReappliesItsTail_AndResolvesOnlyItself()
    {
        var walk = new Walk(
            ("stuck", Stuck),
            ("stored", Imported),
            ("new", Imported),
            ("stored-amendment", Imported)
        );

        var result = await walk.Run(
            pending: ["stuck", "new"],
            processed: ["stored", "stored-amendment"]
        );

        walk.Attempted.Should()
            .Equal(
                ["stuck", "new", "stored-amendment"],
                "the amendment after the change re-applies"
            );
        result.Resolvable.Should().Equal(["new"]);
        result.Complete.Should().BeFalse("the older stuck filing still anchors the next pass");
        walk.Recorded.Should().Equal(["new", "stored-amendment"]);
    }

    [Fact]
    public async Task PassAfterTheLaterFilingResolved_AttemptsOnlyTheStuckFiling()
    {
        var walk = new Walk(
            ("stuck", Stuck),
            ("stored", Imported),
            ("new", Imported),
            ("stored-amendment", Imported)
        );

        var result = await walk.Run(
            pending: ["stuck"],
            processed: ["stored", "new", "stored-amendment"]
        );

        walk.Attempted.Should().Equal(["stuck"]);
        result.Skipped.Should().Be(3);
    }

    [Fact]
    public async Task StuckFilingThatLaterImports_ReappliesEveryLaterFiling_AndCompletes()
    {
        var walk = new Walk(
            ("stuck", Imported),
            ("stored", Imported),
            ("stored-amendment", Imported)
        );

        var result = await walk.Run(pending: ["stuck"], processed: ["stored", "stored-amendment"]);

        walk.Attempted.Should().Equal(["stuck", "stored", "stored-amendment"]);
        result.Complete.Should().BeTrue();
        result.Resolvable.Should().Equal(["stuck"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFailureThatMayHaveWritten_ReappliesEveryLaterFiling(bool failed)
    {
        var outcome = failed ? EntryImportOutcome.Failed : EntryImportOutcome.Incomplete;
        var walk = new Walk(("partial", new EntryImport(outcome, false)), ("stored", Imported));

        var result = await walk.Run(pending: ["partial"], processed: ["stored"]);

        walk.Attempted.Should().Equal(["partial", "stored"], "a possible write reorders the book");
        result.Resolvable.Should().BeEmpty();
        result.Complete.Should().BeFalse();
    }

    [Fact]
    public async Task UnreadableSource_DoesNotStartTheReplay()
    {
        var walk = new Walk(
            ("unreadable", new EntryImport(EntryImportOutcome.Failed, true)),
            ("stored", Imported)
        );

        await walk.Run(pending: ["unreadable"], processed: ["stored"]);

        walk.Attempted.Should().Equal(["unreadable"]);
    }

    [Fact]
    public async Task NeverProcessedFiling_AlwaysImports_AndReappliesItsTail()
    {
        var walk = new Walk(("stuck", Stuck), ("missing", Imported), ("stored", Imported));

        var result = await walk.Run(pending: ["stuck"], processed: ["stored"]);

        walk.Attempted.Should().Equal(["stuck", "missing", "stored"]);
        result.Resolvable.Should().BeEmpty("the missing filing was never pending");
    }

    [Fact]
    public async Task EveryAttemptImported_IsComplete_AndResolvesEveryPendingFiling()
    {
        var walk = new Walk(("first", Imported), ("stored", Imported), ("second", Imported));

        var result = await walk.Run(pending: ["first", "second"], processed: ["stored"]);

        walk.Attempted.Should().Equal(["first", "stored", "second"]);
        result.Complete.Should().BeTrue();
        result.Resolvable.Should().Equal(["first", "second"]);
        result.Imported.Should().Be(3);
    }

    [Fact]
    public async Task PendingFilingFollowedByAFailure_IsNotResolved()
    {
        var walk = new Walk(
            ("first", Imported),
            ("broken", new EntryImport(EntryImportOutcome.Failed, false))
        );

        var result = await walk.Run(pending: ["first"], processed: ["broken"]);

        result.Resolvable.Should().BeEmpty("a later filing in its tail did not import");
        result.Complete.Should().BeFalse();
    }

    [Fact]
    public async Task InterruptedWalk_MarksNothing_SoTheNextWalkReimportsTheWriter()
    {
        var walk = new Walk(
            ("stuck", Stuck),
            ("unmarked", Imported),
            ("stored-amendment", Imported)
        );
        walk.CancelAt = "stored-amendment";

        var run = () => walk.Run(pending: ["stuck"], processed: ["stored-amendment"]);

        await run.Should().ThrowAsync<OperationCanceledException>();
        walk.Attempted.Should().Equal(["stuck", "unmarked", "stored-amendment"]);
        walk.Recorded.Should()
            .BeEmpty(
                "a marked writer would be skipped while the amendment behind it stays unapplied"
            );
    }

    private sealed class Walk(params (string Accession, EntryImport Result)[] steps)
    {
        public List<string> Attempted { get; } = [];
        public List<string> Recorded { get; } = [];
        public string CancelAt { get; set; }

        // The callers build both sets case-insensitively, as the ledger does.
        public Task<RecoveryReplayResult> Run(string[] pending, string[] processed)
        {
            var entries = steps
                .Select(
                    (step, index) =>
                        new EdgarDailyIndexEntry
                        {
                            Cik = "1",
                            AccessionNumber = step.Accession,
                            DateFiled = new DateOnly(2026, 1, 1).AddDays(index),
                            FormType = "13F-HR",
                        }
                )
                .ToList();
            var results = steps.ToDictionary(step => step.Accession, step => step.Result);
            return WalkRecoveryTail(
                entries,
                pending.ToHashSet(StringComparer.OrdinalIgnoreCase),
                processed.ToHashSet(StringComparer.OrdinalIgnoreCase),
                entry =>
                {
                    Attempted.Add(entry.AccessionNumber);
                    if (entry.AccessionNumber == CancelAt)
                        throw new OperationCanceledException();
                    return Task.FromResult(results[entry.AccessionNumber]);
                },
                accessions =>
                {
                    Recorded.AddRange(accessions);
                    return Task.CompletedTask;
                },
                CancellationToken.None
            );
        }
    }
}
