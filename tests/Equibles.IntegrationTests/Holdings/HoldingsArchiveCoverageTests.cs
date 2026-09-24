using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class HoldingsArchiveCoverageTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AuditLatest_UnreadableArchive_RetainsIncompleteCoverageMarker()
    {
        await using var db = fixture.CreateDbContext();
        db.Add(
            new ProcessedDataSet
            {
                FileName = "01jun2026-31aug2026_form13f.zip",
                ParserVersion = ProcessedDataSet.CurrentParserVersion,
            }
        );
        await db.SaveChangesAsync();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .DownloadStream(Arg.Any<string>())
            .Returns(Task.FromException<Stream>(new HttpRequestException("Unavailable")));
        var client = new HoldingsDataSetClient(
            edgar,
            Substitute.For<ILogger<HoldingsDataSetClient>>()
        );
        var processed = new ProcessedDataSetRepository(db);
        var audit = new HoldingsArchiveCoverageService(
            null,
            client,
            processed,
            null,
            null,
            null,
            new HoldingsRealtimeReplaySignal(),
            Substitute.For<ILogger<HoldingsArchiveCoverageService>>()
        );
        var act = () => audit.AuditLatest(new DateOnly(2020, 1, 1), CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
        (await processed.GetByFileName(ProcessedDataSet.CoverageAuditPendingFileName).AnyAsync())
            .Should()
            .BeTrue();
        var coverage = new Equibles.Holdings.BusinessLogic.HoldingsImportCoverage(
            new HoldingsImportFailureRepository(db),
            processed,
            new RealtimeSweepStateRepository(db)
        );
        (await coverage.GetIncompleteReason(new DateOnly(2026, 6, 30)))
            .Should()
            .Contain("being reconciled");
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    public async Task Audit_ChecksExactSourcePositions_WithoutRestoringLaterExits(
        bool present,
        bool later,
        int failuresExpected
    )
    {
        await using var db = fixture.CreateDbContext();
        var issuer = new EquityIssuer { Name = "Source issuer" };
        var holder = new InstitutionalHolder { Cik = "123", Name = "Source manager" };
        db.AddRange(issuer, holder);
        db.Add(
            new EquityListingCusipEvidence
            {
                EquityIssuerId = issuer.Id,
                Cusip = "78464A805",
                ListedTicker = "SPTM",
            }
        );
        var quarter = new DateOnly(2026, 6, 30);
        if (present)
            db.Add(
                new InstitutionalHolding
                {
                    EquityIssuerId = issuer.Id,
                    InstitutionalHolderId = holder.Id,
                    Cusip = "78464A805",
                    ListedTicker = "SPTM",
                    ReportDate = quarter,
                    FilingDate = new(2026, 8, 19),
                    FilingType = FilingType.Form13F,
                    ShareType = ShareType.Shares,
                    Shares = 32342,
                    AccessionNumber = "original",
                }
            );
        if (later)
            db.Add(
                new InstitutionalFiling
                {
                    InstitutionalHolderId = holder.Id,
                    AccessionNumber = "restatement",
                    ReportDate = quarter,
                    FilingDate = new(2026, 9, 1),
                    FilingType = FilingType.Form13F,
                    IsAmendment = true,
                }
            );
        db.Add(
            new ProcessedDataSet
            {
                FileName = "01jun2026-31aug2026_form13f.zip",
                ParserVersion = ProcessedDataSet.CurrentParserVersion,
            }
        );
        await db.SaveChangesAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => fixture.CreateDbContext());
        services.AddScoped<EquityIssuerRepository>();
        using var provider = services.BuildServiceProvider();
        var importer = new HoldingsImportService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<IStockPriceProvider>(),
            Substitute.For<IBus>()
        );
        var failures = new HoldingsImportFailureRepository(db);
        var service = new HoldingsArchiveCoverageService(
            importer,
            null,
            new ProcessedDataSetRepository(db),
            new InstitutionalHolderRepository(db),
            new InstitutionalHoldingRepository(db),
            failures,
            new HoldingsRealtimeReplaySignal(),
            Substitute.For<ILogger<HoldingsArchiveCoverageService>>()
        );
        using var stream = new MemoryStream();
        using (var output = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(
                output,
                "SUBMISSION.tsv",
                "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n13F-HR\toriginal\t19-AUG-2026\t30-JUN-2026\t0000000123\n"
            );
            Write(
                output,
                "COVERPAGE.tsv",
                "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\noriginal\tN\tSource manager\n"
            );
            Write(
                output,
                "INFOTABLE.tsv",
                "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMTTYPE\tPUTCALL\noriginal\t78464A805\tSH\t\n"
            );
        }
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        await service.Audit(archive, new DateOnly(2020, 1, 1), CancellationToken.None);
        var rows = await failures.GetAll().ToListAsync();
        rows.Should().HaveCount(failuresExpected);
        if (failuresExpected > 0)
        {
            rows.Single().Reason.Should().Be(HoldingsImportFailureReason.MissingSourcePosition);
            rows.Single().AccessionNumber.Should().Be("original");
            await service.Audit(archive, new DateOnly(2020, 1, 1), CancellationToken.None);
            (await failures.GetAll().AsNoTracking().SingleAsync()).Attempts.Should().Be(1);
        }
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(
            archive.CreateEntry(name).Open(),
            new UTF8Encoding(false)
        );
        writer.Write(text);
    }
}
