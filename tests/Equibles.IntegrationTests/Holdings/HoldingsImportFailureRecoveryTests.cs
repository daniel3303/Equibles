using Equibles.Holdings.BusinessLogic;
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
public class HoldingsImportFailureRecoveryTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    private static readonly DateOnly Quarter = new(2026, 6, 30);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Failure_SurvivesNewContext_AndReopensAfterResolution()
    {
        await using (var first = fixture.CreateDbContext())
            await new HoldingsImportFailureRepository(first).Record(
                "original",
                "123",
                Quarter,
                Quarter,
                HoldingsImportFailureReason.IdentityConflict,
                CancellationToken.None
            );
        await using var second = fixture.CreateDbContext();
        var repo = new HoldingsImportFailureRepository(second);
        var row = await repo.GetAll().AsNoTracking().SingleAsync();
        row.ResolvedAt.Should().BeNull();
        row.NextAttemptAt.Should().BeOnOrAfter(row.LastAttemptAt);
        await repo.Resolve("original", CancellationToken.None);
        await repo.Record(
            "original",
            "123",
            Quarter,
            Quarter,
            HoldingsImportFailureReason.Incomplete,
            CancellationToken.None
        );
        row = await repo.GetAll().AsNoTracking().SingleAsync();
        row.Attempts.Should().Be(2);
        row.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task Resolution_DoesNotClearAConcurrentNewFailure()
    {
        await using var db = fixture.CreateDbContext();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.Record(
            "original",
            "123",
            Quarter,
            Quarter,
            HoldingsImportFailureReason.Incomplete,
            CancellationToken.None
        );
        var first = await failures.GetAll().AsNoTracking().SingleAsync();
        await failures.Record(
            "original",
            "123",
            Quarter,
            Quarter,
            HoldingsImportFailureReason.IdentityConflict,
            CancellationToken.None
        );
        await failures.Resolve("original", CancellationToken.None, first.LastAttemptAt);
        (await failures.GetAll().AsNoTracking().SingleAsync()).ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task Recovery_RefusesToOverwriteARetainedAmendmentMissingFromTheSourceResponse()
    {
        await using var db = fixture.CreateDbContext();
        var issuer = new Equibles.CommonStocks.Data.Models.EquityIssuer { Name = "Issuer" };
        var holder = new InstitutionalHolder { Cik = "123", Name = "Manager" };
        db.AddRange(issuer, holder);
        db.Add(
            new InstitutionalHolding
            {
                EquityIssuerId = issuer.Id,
                InstitutionalHolderId = holder.Id,
                ReportDate = Quarter,
                FilingDate = new(2026, 9, 1),
                FilingType = FilingType.Form13F,
                AccessionNumber = "retained-amendment",
                Shares = 17,
            }
        );
        await db.SaveChangesAsync();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.Record(
            "original",
            "123",
            new DateOnly(2026, 8, 1),
            Quarter,
            HoldingsImportFailureReason.Incomplete,
            CancellationToken.None
        );
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetCompanyFilings("123", null, new DateOnly(2026, 8, 1), Arg.Any<DateOnly?>())
            .Returns(
                new List<FilingData>
                {
                    new()
                    {
                        AccessionNumber = "original",
                        Form = "13F-HR",
                        FilingDate = new(2026, 8, 1),
                        ReportDate = Quarter,
                    },
                }
            );
        var ingestion = Substitute.For<Realtime13FIngestionService>(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            null,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );
        var recovery = new HoldingsImportRecoveryService(
            failures,
            new InstitutionalHoldingRepository(db),
            edgar,
            ingestion,
            Substitute.For<ILogger<HoldingsImportRecoveryService>>()
        );
        await recovery.Recover(new DateOnly(2020, 1, 1), CancellationToken.None);
        await ingestion
            .DidNotReceive()
            .IngestSpecificFilings(
                Arg.Any<IReadOnlyCollection<EdgarDailyIndexEntry>>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            );
        (await failures.GetAll().AsNoTracking().SingleAsync()).ResolvedAt.Should().BeNull();
        (await db.Set<InstitutionalHolding>().SingleAsync()).Shares.Should().Be(17);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recovery_ReplaysLaterAmendments_AndOnlyResolvesAfterWholeTail(bool complete)
    {
        await using var db = fixture.CreateDbContext();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.Record(
            "original",
            "123",
            new DateOnly(2026, 8, 1),
            Quarter,
            HoldingsImportFailureReason.Incomplete,
            CancellationToken.None
        );
        await failures
            .GetAll()
            .ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.NextAttemptAt, DateTime.UtcNow.AddDays(-1))
            );
        db.Set<ProcessedFiling>().Add(new ProcessedFiling { AccessionNumber = "amendment" });
        await db.SaveChangesAsync();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetCompanyFilings("123", null, new DateOnly(2026, 8, 1), Arg.Any<DateOnly?>())
            .Returns(
                new List<FilingData>
                {
                    new()
                    {
                        AccessionNumber = "original",
                        Form = "13F-HR",
                        FilingDate = new(2026, 8, 1),
                        ReportDate = Quarter,
                    },
                    new()
                    {
                        AccessionNumber = "amendment",
                        Form = "13F-HR/A",
                        FilingDate = new(2026, 9, 1),
                        ReportDate = Quarter,
                    },
                }
            );
        var ingestion = Substitute.For<Realtime13FIngestionService>(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            null,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );
        ingestion
            .IngestSpecificFilings(
                Arg.Any<IReadOnlyCollection<EdgarDailyIndexEntry>>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(complete ? 2 : 1);
        var recovery = new HoldingsImportRecoveryService(
            failures,
            new InstitutionalHoldingRepository(db),
            edgar,
            ingestion,
            Substitute.For<ILogger<HoldingsImportRecoveryService>>()
        );
        await recovery.Recover(new DateOnly(2020, 1, 1), CancellationToken.None);
        await ingestion
            .Received(1)
            .IngestSpecificFilings(
                Arg.Is<IReadOnlyCollection<EdgarDailyIndexEntry>>(entries =>
                    entries.Count == 2 && entries.Any(e => e.AccessionNumber == "amendment")
                ),
                new DateOnly(2020, 1, 1),
                Arg.Any<CancellationToken>()
            );
        var row = await failures.GetAll().AsNoTracking().SingleAsync();
        (row.ResolvedAt != null).Should().Be(complete);
    }

    [Fact]
    public async Task Coverage_ReportsFailuresEvenWhenArchiveIsProcessed()
    {
        await using var db = fixture.CreateDbContext();
        db.Add(
            new ProcessedDataSet
            {
                FileName = "01jun2026-31aug2026_form13f.zip",
                ParserVersion = ProcessedDataSet.CurrentParserVersion,
            }
        );
        db.Add(
            new RealtimeSweepState
            {
                WorkerName = "Holdings13FRealtime",
                SweptThrough = DateOnly.FromDateTime(DateTime.UtcNow),
            }
        );
        await db.SaveChangesAsync();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.Record(
            "original",
            "123",
            Quarter,
            Quarter,
            HoldingsImportFailureReason.IdentityConflict,
            CancellationToken.None
        );
        var coverage = new HoldingsImportCoverage(
            failures,
            new ProcessedDataSetRepository(db),
            new RealtimeSweepStateRepository(db)
        );
        (await coverage.GetIncompleteReason(Quarter)).Should().Contain("not finished importing");
        await failures.Resolve("original", CancellationToken.None);
        (await coverage.GetIncompleteReason(Quarter)).Should().BeNull();
    }
}
