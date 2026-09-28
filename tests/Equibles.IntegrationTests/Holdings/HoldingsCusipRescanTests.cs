using System.IO.Compression;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Consumers;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class HoldingsCusipRescanTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    private const string First = "2023q1_form13f.zip";
    private const string Second = "2023q2_form13f.zip";
    private static readonly DateOnly Floor = new(2020, 1, 1);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static HoldingsCusipRescanService Service(
        EquiblesFinancialDbContext db,
        ISecEdgarClient edgar,
        HoldingsCusipRescanRepository requests = null
    ) =>
        new(
            requests ?? new HoldingsCusipRescanRepository(db),
            new ProcessedDataSetRepository(db),
            new HoldingsDataSetClient(edgar, Substitute.For<ILogger<HoldingsDataSetClient>>()),
            new HoldingsImportFailureRepository(db),
            new HoldingsRescanSignal(),
            new HoldingsRealtimeReplaySignal(),
            Substitute.For<ILogger<HoldingsCusipRescanService>>()
        );

    private async Task Seed()
    {
        await using var db = fixture.CreateDbContext();
        db.AddRange(
            new ProcessedDataSet
            {
                FileName = First,
                ParserVersion = ProcessedDataSet.CurrentParserVersion,
            },
            new ProcessedDataSet
            {
                FileName = Second,
                ParserVersion = ProcessedDataSet.CurrentParserVersion,
            },
            new HoldingsCusipRescan
            {
                EquityIssuerId = Guid.NewGuid(),
                Ticker = "TEST",
                PreviousCusip = "OLD",
                Cusip = "NEW",
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task InterruptedScan_RestartsAtNextArchive_PreservesLedger_AndFindsMissingHoldingsFromSource()
    {
        await Seed();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .DownloadStream(Arg.Is<string>(url => url.EndsWith(First)))
            .Returns(_ => Source("OLD"));
        edgar
            .DownloadStream(Arg.Is<string>(url => url.EndsWith(Second)))
            .Returns<Task<Stream>>(_ => throw new IOException("unavailable"));
        await using (var db = fixture.CreateDbContext())
            await Service(db, edgar)
                .Invoking(service => service.Scan(Floor, CancellationToken.None))
                .Should()
                .ThrowAsync<IOException>();
        await using (var db = fixture.CreateDbContext())
        {
            (await db.Set<HoldingsCusipRescan>().SingleAsync())
                .ScannedThrough.Should()
                .Be(new DateOnly(2023, 3, 31));
            (await db.Set<HoldingsCusipRescan>().SingleAsync()).CompletedAt.Should().BeNull();
            (await db.Set<InstitutionalHolding>().CountAsync()).Should().Be(0);
            var failure = await db.Set<HoldingsImportFailure>().SingleAsync();
            failure.AccessionNumber.Should().Be("original");
            failure.Cik.Should().Be("123");
            (await db.Set<ProcessedDataSet>().CountAsync()).Should().Be(2);
            var coverage = new HoldingsImportCoverage(
                new HoldingsImportFailureRepository(db),
                new ProcessedDataSetRepository(db),
                new RealtimeSweepStateRepository(db),
                new HoldingsCusipRescanRepository(db)
            );
            (await coverage.GetIncompleteReason(default)).Should().Contain("being reconciled");
            // An identity arriving after the first archive must start its own scan at the beginning.
            db.Add(new HoldingsCusipRescan { EquityIssuerId = Guid.NewGuid(), Cusip = "OTHER" });
            await db.SaveChangesAsync();
        }
        edgar
            .DownloadStream(Arg.Is<string>(url => url.EndsWith(Second)))
            .Returns(_ => Source("NONE"));
        await using (var db = fixture.CreateDbContext())
            await Service(db, edgar).Scan(Floor, CancellationToken.None);
        await edgar.Received(2).DownloadStream(Arg.Is<string>(url => url.EndsWith(First)));
        await edgar.Received(2).DownloadStream(Arg.Is<string>(url => url.EndsWith(Second)));
        await using (var db = fixture.CreateDbContext())
        {
            (await db.Set<HoldingsCusipRescan>().CountAsync(row => row.CompletedAt != null))
                .Should()
                .Be(2);
            (await db.Set<ProcessedDataSet>().Select(row => row.FileName).ToListAsync())
                .Should()
                .BeEquivalentTo(First, Second, ProcessedDataSet.RealtimeReplayPendingFileName);
            (await db.Set<HoldingsImportFailure>().Select(row => row.Cik).ToListAsync())
                .Should()
                .BeEquivalentTo("123", "456");
        }
    }

    [Fact]
    public async Task FailedCheckpoint_RollsBackQueuedRecoveries_AndLeavesArchiveEligible()
    {
        await Seed();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar.DownloadStream(Arg.Any<string>()).Returns(_ => Source("NEW"));
        await using (var db = fixture.CreateDbContext())
            await Service(db, edgar, new FailSave(db))
                .Invoking(service => service.Scan(Floor, CancellationToken.None))
                .Should()
                .ThrowAsync<IOException>();
        await using var verify = fixture.CreateDbContext();
        (await verify.Set<HoldingsImportFailure>().CountAsync()).Should().Be(0);
        (await verify.Set<HoldingsCusipRescan>().SingleAsync()).ScannedThrough.Should().BeNull();
    }

    [Fact]
    public async Task NewIdentityRecovery_CannotBeAcknowledgedByOlderInFlightRecovery()
    {
        await using var db = fixture.CreateDbContext();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.EnqueueRecovery(
            "original",
            "123",
            new DateOnly(2023, 2, 1),
            CancellationToken.None
        );
        var startedBeforeIdentityChange = DateTime.UtcNow.AddMinutes(-1);
        await db.Set<HoldingsImportFailure>()
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(
                    row => row.LastAttemptAt,
                    startedBeforeIdentityChange.AddMinutes(-1)
                )
            );
        await failures.EnqueueRecovery(
            "original",
            "123",
            new DateOnly(2023, 2, 1),
            CancellationToken.None,
            supersedesActiveAttempt: true
        );
        await failures.Resolve("original", CancellationToken.None, startedBeforeIdentityChange);
        (await db.Set<HoldingsImportFailure>().SingleAsync()).ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task RedeliveredMessage_DoesNotResetCompletedOrInProgressRequests()
    {
        await using var db = fixture.CreateDbContext();
        var context = Substitute.For<ConsumeContext<StockCusipChanged>>();
        var id = Guid.NewGuid();
        context.MessageId.Returns(id);
        context.Message.Returns(new StockCusipChanged(Guid.NewGuid(), "TEST", "OLD", "NEW"));
        var consumer = new StockCusipChangedConsumer(
            new HoldingsCusipRescanRepository(db),
            new HoldingsRescanSignal(),
            Substitute.For<ILogger<StockCusipChangedConsumer>>()
        );
        await consumer.Consume(context);
        await db.Set<HoldingsCusipRescan>()
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(row => row.ScannedThrough, new DateOnly(2023, 3, 31))
            );
        await consumer.Consume(context);
        (await db.Set<HoldingsCusipRescan>().CountAsync()).Should().Be(1);
        (await db.Set<HoldingsCusipRescan>().SingleAsync())
            .ScannedThrough.Should()
            .Be(new DateOnly(2023, 3, 31));
    }

    [Theory]
    [InlineData("bad-header", "CUSIP\nNEW\n")]
    [InlineData("missing-accession", "ACCESSION_NUMBER\tCUSIP\nabsent\tNEW\n")]
    [InlineData("short-row", "ACCESSION_NUMBER\tCUSIP\noriginal\n")]
    [InlineData("blank-accession", "ACCESSION_NUMBER\tCUSIP\n\tNEW\n")]
    [InlineData("duplicate-column", "ACCESSION_NUMBER\tCUSIP\tcusip\noriginal\tOTHER\tNEW\n")]
    public async Task MalformedSource_NeverAdvancesArchiveCursor(string _, string info)
    {
        await Seed();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar.DownloadStream(Arg.Any<string>()).Returns(_ => Source("NEW", info));
        await using (var db = fixture.CreateDbContext())
            await Service(db, edgar)
                .Invoking(service => service.Scan(Floor, CancellationToken.None))
                .Should()
                .ThrowAsync<InvalidDataException>();
        await using var verify = fixture.CreateDbContext();
        (await verify.Set<HoldingsCusipRescan>().SingleAsync()).ScannedThrough.Should().BeNull();
        (await verify.Set<HoldingsImportFailure>().CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("ACCESSION_NUMBER\tCUSIP\noriginal\tNEW\textra\n")]
    [InlineData("UNUSED\taccession_number\tcusip\tOPTIONAL\n\toriginal\tNEW\n")]
    public async Task UnusedColumns_DoNotBlockValidSourceRecovery(string info)
    {
        await Seed();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar.DownloadStream(Arg.Any<string>()).Returns(_ => Source("NEW", info));
        await using (var db = fixture.CreateDbContext())
            await Service(db, edgar).Scan(Floor, CancellationToken.None);
        await using var verify = fixture.CreateDbContext();
        (await verify.Set<HoldingsCusipRescan>().SingleAsync()).CompletedAt.Should().NotBeNull();
        (await verify.Set<HoldingsImportFailure>().SingleAsync())
            .AccessionNumber.Should()
            .Be("original");
    }

    private sealed class FailSave(EquiblesFinancialDbContext db) : HoldingsCusipRescanRepository(db)
    {
        public override Task SaveChanges() => throw new IOException("checkpoint write failed");
    }

    private static Stream Source(string cusip, string info = null)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("INFOTABLE.tsv").Open()))
                writer.Write(
                    info
                        ?? $"ACCESSION_NUMBER\tCUSIP\noriginal\t{cusip}\nlater\t{cusip}\nunrelated\tOTHER\n"
                );
            using (var writer = new StreamWriter(archive.CreateEntry("SUBMISSION.tsv").Open()))
                writer.Write(
                    "ACCESSION_NUMBER\tCIK\tFILING_DATE\tPERIODOFREPORT\tSUBMISSIONTYPE\n"
                        + "original\t0000123\t01-FEB-2023\t31-DEC-2022\t13F-HR\n"
                        + "later\t0000123\t15-FEB-2023\t31-DEC-2022\t13F-HR/A\n"
                        + "unrelated\t0000456\t01-FEB-2023\t31-DEC-2022\t13F-HR\n"
                );
        }
        stream.Position = 0;
        return stream;
    }
}
