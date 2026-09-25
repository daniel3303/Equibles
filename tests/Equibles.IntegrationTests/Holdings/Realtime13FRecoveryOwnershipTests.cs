using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class Realtime13FRecoveryOwnershipTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    private readonly List<EquiblesFinancialDbContext> contexts = [];
    private static readonly DateOnly Filed = new(2026, 9, 20);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var context in contexts)
            context.Dispose();
        return Task.CompletedTask;
    }

    private IServiceScopeFactory Scopes()
    {
        var factory = Substitute.For<IServiceScopeFactory>();
        factory
            .CreateScope()
            .Returns(_ =>
            {
                var db = fixture.CreateDbContext();
                contexts.Add(db);
                var provider = Substitute.For<IServiceProvider>();
                provider
                    .GetService(typeof(ProcessedFilingRepository))
                    .Returns(new ProcessedFilingRepository(db));
                provider
                    .GetService(typeof(HoldingsImportFailureRepository))
                    .Returns(new HoldingsImportFailureRepository(db));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        return factory;
    }

    private Realtime13FIngestionService Ingestion(ISecEdgarClient edgar) =>
        new(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            null,
            Scopes(),
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );

    private static EdgarDailyIndexEntry Entry(
        string accession,
        DateOnly filed,
        string cik = "000123"
    ) =>
        new()
        {
            AccessionNumber = accession,
            Cik = cik,
            DateFiled = filed,
            FormType = "13F-HR",
        };

    [Fact]
    public async Task Sweep_QueuesOlderAndNewerDiscoveries_WithoutRepeatingImportsOrDeferringFailures()
    {
        await using var db = fixture.CreateDbContext();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.Record(
            "pending",
            "123",
            Filed,
            null,
            HoldingsImportFailureReason.Incomplete,
            CancellationToken.None
        );
        await failures.Defer("123", DateTime.UtcNow, CancellationToken.None);
        var before = await failures.GetAll().AsNoTracking().SingleAsync();
        db.Add(new ProcessedFiling { AccessionNumber = "processed" });
        await db.SaveChangesAsync();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDailyIndex(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([
                Entry("pending", Filed),
                Entry("older", Filed.AddDays(-10)),
                Entry("new-tail", Filed.AddDays(1)),
                Entry("processed", Filed, "456"),
            ]);
        var ingestion = Ingestion(edgar);
        for (var pass = 0; pass < 2; pass++)
        {
            var result = await ingestion.IngestRecentFilings(
                Filed.AddDays(1),
                1,
                new(2020, 1, 1),
                CancellationToken.None
            );
            result.FilingsImported.Should().Be(0);
            result.EarliestFailedDate.Should().BeNull();
        }
        await edgar
            .DidNotReceive()
            .GetFilingArtifactNames(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        var rows = await failures.GetAll().AsNoTracking().ToListAsync();
        rows.Should().HaveCount(3);
        var pending = rows.Single(r => r.AccessionNumber == "pending");
        pending.Attempts.Should().Be(before.Attempts);
        pending.NextAttemptAt.Should().Be(before.NextAttemptAt);
        pending.LastAttemptAt.Should().Be(before.LastAttemptAt);
        rows.Single(r => r.AccessionNumber == "older").FilingDate.Should().Be(Filed.AddDays(-10));
        rows.Where(r => r.AccessionNumber != "pending")
            .Should()
            .OnlyContain(r =>
                r.Cik == "123"
                && r.Attempts == 0
                && r.Reason == HoldingsImportFailureReason.PendingRecovery
            );
        (await db.Set<ProcessedFiling>().CountAsync()).Should().Be(1);

        // The newly discovered tail is durable evidence even if submissions omits it.
        edgar
            .GetCompanyFilings("123", null, Filed.AddDays(-10), Arg.Any<DateOnly?>())
            .Returns([
                new FilingData
                {
                    AccessionNumber = "older",
                    Form = "13F-HR",
                    FilingDate = Filed.AddDays(-10),
                },
                new FilingData
                {
                    AccessionNumber = "pending",
                    Form = "13F-HR",
                    FilingDate = Filed,
                },
            ]);
        var recoveryIngestion = Substitute.For<Realtime13FIngestionService>(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            null,
            Scopes(),
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );
        var recovery = new HoldingsImportRecoveryService(
            failures,
            new InstitutionalHoldingRepository(db),
            edgar,
            recoveryIngestion,
            Substitute.For<ILogger<HoldingsImportRecoveryService>>()
        );
        await recovery.Recover(new(2020, 1, 1), CancellationToken.None);
        await recoveryIngestion
            .DidNotReceive()
            .IngestSpecificFilings(
                Arg.Any<IReadOnlyCollection<EdgarDailyIndexEntry>>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            );
        (await failures.GetAll().CountAsync(r => r.ResolvedAt == null)).Should().Be(3);
    }

    [Fact]
    public async Task NewFailure_ReplaysProcessedLaterFiling_AndDoesNotBlockOtherManagers()
    {
        await using var db = fixture.CreateDbContext();
        db.Add(new ProcessedFiling { AccessionNumber = "amendment" });
        await db.SaveChangesAsync();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDailyIndex(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([
                Entry("original", Filed),
                Entry("amendment", Filed.AddDays(1)),
                Entry("unrelated", Filed, "456"),
            ]);
        // Unreadable artifacts exercise the real failure writer without any importer stub.
        edgar
            .GetFilingArtifactNames(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns([]);
        var result = await Ingestion(edgar)
            .IngestRecentFilings(Filed.AddDays(1), 1, new(2020, 1, 1), CancellationToken.None);
        result.EarliestFailedDate.Should().Be(Filed);
        foreach (var accession in new[] { "original", "amendment", "unrelated" })
            await edgar
                .Received(1)
                .GetFilingArtifactNames(Arg.Any<string>(), accession, Arg.Any<CancellationToken>());
        (await db.Set<HoldingsImportFailure>().CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Enqueue_ReopensResolvedIntent_AndCancellationDoesNotInsert()
    {
        await using var db = fixture.CreateDbContext();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.EnqueueRecovery("original", "123", Filed, CancellationToken.None);
        await failures.Resolve("original", CancellationToken.None);
        await failures.EnqueueRecovery("original", "123", Filed, CancellationToken.None);
        var row = await failures.GetAll().AsNoTracking().SingleAsync();
        row.ResolvedAt.Should().BeNull();
        row.Attempts.Should().Be(0);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            failures.EnqueueRecovery("cancelled", "123", Filed, cancellation.Token)
        );
        (await failures.GetAll().CountAsync()).Should().Be(1);
    }
}
