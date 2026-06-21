using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Repositories;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Adversarial: the retry ceiling must bound the <em>success-but-still-NotChecked</em>
/// (<c>Skipped</c>) path, not just the fetch-exception path. A document whose fetch keeps
/// succeeding while capture keeps yielding <see cref="XbrlCaptureStatus.NotChecked"/> (capture
/// disabled, or extraction repeatedly errors) stays <c>NotChecked</c> and — like a permanently
/// unfetchable filing — would sit at the head of the newest-first queue forever unless its
/// bumped attempt count is persisted on this path too. Only the exception path is covered today.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class XbrlBackfillServiceSkippedCeilingTests : ParadeDbMcpTestBase
{
    public XbrlBackfillServiceSkippedCeilingTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private async Task<CommonStock> SeedCompany()
    {
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        await using var seed = Fixture.CreateDbContext();
        seed.Set<CommonStock>().Add(apple);
        await seed.SaveChangesAsync();
        return apple;
    }

    private async Task SeedDocument(Guid companyId, string accessionNumber, DateOnly reportingDate)
    {
        await using var seed = Fixture.CreateDbContext();
        var content = new File
        {
            Id = Guid.NewGuid(),
            Name = "content",
            Extension = "txt",
            ContentType = "text/plain",
            Size = 4,
            FileContent = new() { Bytes = "body"u8.ToArray() },
        };
        seed.Set<File>().Add(content);
        seed.Set<Document>()
            .Add(
                new Document
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = companyId,
                    Content = content,
                    DocumentType = DocumentType.TenK,
                    ReportingDate = reportingDate,
                    ReportingForDate = reportingDate,
                    AccessionNumber = accessionNumber,
                    XbrlStatus = XbrlCaptureStatus.NotChecked,
                }
            );
        await seed.SaveChangesAsync();
    }

    // Capture disabled => Capture() returns NotChecked on every successful fetch, the same
    // terminal-less outcome an internally-erroring extraction produces. The fetch itself
    // always succeeds, so each cycle takes the Skipped branch.
    private XbrlBackfillService BuildSutWithCaptureDisabled()
    {
        var repo = new DocumentRepository(DbContext);
        var persistence = new DocumentPersistenceService(
            repo,
            new ChunkRepository(DbContext),
            new FileManager(new FileRepository(DbContext)),
            Substitute.For<IBus>()
        );
        var capture = new XbrlEnvelopeCaptureService(
            Options.Create(new XbrlCaptureOptions { Enabled = false }),
            NullLogger<XbrlEnvelopeCaptureService>()
        );
        var client = Substitute.For<ISecEdgarClient>();
        client.GetDocumentContent(Arg.Any<string>(), Arg.Any<string>()).Returns("irrelevant body");
        return new XbrlBackfillService(
            repo,
            client,
            capture,
            persistence,
            NullLogger<XbrlBackfillService>()
        );
    }

    [Fact]
    public async Task Backfill_DocumentStuckOnSkippedPath_StopsBeingSelectedAfterRetryCeiling()
    {
        const int maxAttempts = 5;
        var company = await SeedCompany();
        await SeedDocument(company.Id, "STUCK-SKIPPED", new DateOnly(2024, 3, 1));
        DbContext.ChangeTracker.Clear();

        var sut = BuildSutWithCaptureDisabled();

        // It must be selected exactly MaxAttempts times: each cycle bumps and persists the
        // attempt count via the Skipped path's UpdateXbrl save.
        for (var cycle = 0; cycle < maxAttempts; cycle++)
        {
            var cycleResult = await sut.Backfill(batchSize: 1, null);
            cycleResult.Processed.Should().Be(1, $"cycle {cycle} should still pick the document");
            cycleResult.Skipped.Should().Be(1, $"cycle {cycle} keeps it NotChecked");
        }

        // One cycle past the ceiling: the document must have dropped out of the working set.
        // If the Skipped path failed to persist the bumped attempts, it would be reselected
        // here forever (Processed == 1) — the very starvation the ceiling exists to prevent.
        var afterCeiling = await sut.Backfill(batchSize: 1, null);
        afterCeiling.Processed.Should().Be(0);

        await using var verify = Fixture.CreateDbContext();
        var stuck = await verify
            .Set<Document>()
            .SingleAsync(d => d.AccessionNumber == "STUCK-SKIPPED");
        stuck.XbrlCaptureAttempts.Should().Be(maxAttempts);
        stuck.XbrlStatus.Should().Be(XbrlCaptureStatus.NotChecked);
    }
}
