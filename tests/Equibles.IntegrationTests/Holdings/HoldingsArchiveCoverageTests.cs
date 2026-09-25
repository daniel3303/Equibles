using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
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

    [Theory]
    [InlineData("438516205", null, false, false, 0)]
    [InlineData("438516106", null, false, false, 0)]
    [InlineData("438516205", "RETAINED", false, false, 0)]
    [InlineData("999999999", null, false, false, 1)]
    [InlineData("438516205", null, true, false, 1)]
    [InlineData("438516205", null, false, true, 1)]
    public async Task Audit_MergedSourceCusips_RequiresOneGroundedObservationPerSecurity(
        string storedCusip,
        string storedLabel,
        bool duplicateObservation,
        bool sibling,
        int expectedFailures
    )
    {
        await using var db = fixture.CreateDbContext();
        var issuer = new EquityIssuer { Name = "Merged source issuer" };
        var holder = new InstitutionalHolder { Cik = "1948780", Name = "Merged source manager" };
        db.AddRange(issuer, holder);
        db.Add(new EquityIssuerCusipAlias { EquityIssuerId = issuer.Id, Cusip = "438516205" });
        if (sibling)
            db.Add(
                new EquityListingCusipEvidence
                {
                    EquityIssuerId = issuer.Id,
                    Cusip = "438516106",
                    ListedTicker = "OTHER",
                }
            );
        else
            db.Add(new EquityIssuerCusipAlias { EquityIssuerId = issuer.Id, Cusip = "438516106" });
        db.Add(Position(issuer.Id, holder.Id, storedCusip, storedLabel));
        if (duplicateObservation)
            db.Add(Position(issuer.Id, holder.Id, "438516106", "RETAINED"));
        await db.SaveChangesAsync();

        await AuditArchive(
            db,
            "13F-HR\toriginal\t19-AUG-2026\t30-JUN-2026\t1948780\n",
            "original\tN\t\tMerged source manager\n",
            "original\t438516205\tSH\t\t264131\noriginal\t438516106\tSH\t\t4569\n"
        );
        (await db.Set<HoldingsImportFailure>().CountAsync()).Should().Be(expectedFailures);
        (await db.Set<InstitutionalHolding>().FirstAsync()).Shares.Should().Be(268700);
    }

    [Theory]
    [InlineData("NEW HOLDINGS", false, 0)]
    [InlineData("NEW HOLDINGS", true, 1)]
    [InlineData("RESTATEMENT", true, 0)]
    public async Task Audit_Amendments_ReplaceOnlyTheirOwnPersistenceKeys(
        string amendmentType,
        bool differentSecurity,
        int expectedFailures
    )
    {
        await using var db = fixture.CreateDbContext();
        var issuer = new EquityIssuer { Name = "Amended source issuer" };
        var holder = new InstitutionalHolder { Cik = "1948780", Name = "Amended source manager" };
        db.AddRange(issuer, holder);
        db.Add(new EquityIssuerCusipAlias { EquityIssuerId = issuer.Id, Cusip = "438516106" });
        if (differentSecurity)
            db.Add(
                new EquityListingCusipEvidence
                {
                    EquityIssuerId = issuer.Id,
                    Cusip = "438516205",
                    ListedTicker = "OTHER",
                }
            );
        else
            db.Add(new EquityIssuerCusipAlias { EquityIssuerId = issuer.Id, Cusip = "438516205" });
        var position = Position(
            issuer.Id,
            holder.Id,
            "438516205",
            differentSecurity ? "OTHER" : null
        );
        position.AccessionNumber = "amendment";
        position.FilingDate = new DateOnly(2026, 8, 20);
        db.Add(position);
        await db.SaveChangesAsync();

        await AuditArchive(
            db,
            "13F-HR\toriginal\t19-AUG-2026\t30-JUN-2026\t1948780\n13F-HR/A\tamendment\t20-AUG-2026\t30-JUN-2026\t1948780\n",
            $"original\tN\t\tAmended source manager\namendment\tY\t{amendmentType}\tAmended source manager\n",
            "original\t438516106\tSH\t\t4569\namendment\t438516205\tSH\t\t264131\n"
        );
        (await db.Set<HoldingsImportFailure>().CountAsync()).Should().Be(expectedFailures);
        if (expectedFailures > 0)
            (await db.Set<HoldingsImportFailure>().SingleAsync())
                .AccessionNumber.Should()
                .Be("original");
    }

    private static InstitutionalHolding Position(
        Guid issuer,
        Guid holder,
        string cusip,
        string label
    ) =>
        new()
        {
            EquityIssuerId = issuer,
            InstitutionalHolderId = holder,
            Cusip = cusip,
            ListedTicker = label,
            ReportDate = new DateOnly(2026, 6, 30),
            FilingDate = new DateOnly(2026, 8, 19),
            FilingType = FilingType.Form13F,
            ShareType = ShareType.Shares,
            Shares = 268700,
            AccessionNumber = "original",
        };

    private async Task AuditArchive(
        EquiblesFinancialDbContext db,
        string submissions,
        string covers,
        string positions
    )
    {
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
        var service = new HoldingsArchiveCoverageService(
            importer,
            null,
            new ProcessedDataSetRepository(db),
            new InstitutionalHolderRepository(db),
            new InstitutionalHoldingRepository(db),
            new HoldingsImportFailureRepository(db),
            new HoldingsRealtimeReplaySignal(),
            Substitute.For<ILogger<HoldingsArchiveCoverageService>>()
        );
        using var stream = new MemoryStream();
        using (var output = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(
                output,
                "SUBMISSION.tsv",
                "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n" + submissions
            );
            Write(
                output,
                "COVERPAGE.tsv",
                "ACCESSION_NUMBER\tISAMENDMENT\tAMENDMENTTYPE\tFILINGMANAGER_NAME\n" + covers
            );
            Write(
                output,
                "INFOTABLE.tsv",
                "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMTTYPE\tPUTCALL\tSSHPRNAMT\n" + positions
            );
        }
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        await service.Audit(archive, new DateOnly(2020, 1, 1), CancellationToken.None);
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
