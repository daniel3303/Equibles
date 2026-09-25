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
    [InlineData("438516205", null, false, false, false, 0)]
    [InlineData("438516106", null, false, false, false, 0)]
    [InlineData("438516205", "RETAINED", false, false, false, 0)]
    [InlineData("999999999", null, false, false, false, 1)]
    [InlineData("438516205", null, true, false, false, 0)]
    [InlineData("438516205", null, false, true, false, 1)]
    [InlineData("438516205", null, false, false, true, 1)]
    public async Task Audit_MergedSourceCusips_RequiresOneGroundedObservationPerSecurity(
        string storedCusip,
        string storedLabel,
        bool duplicateObservation,
        bool sibling,
        bool partial,
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
        var stored = Position(issuer.Id, holder.Id, storedCusip, storedLabel);
        if (partial)
            stored.Shares = 264131;
        db.Add(stored);
        if (duplicateObservation)
            db.Add(Position(issuer.Id, holder.Id, "438516106", "RETAINED"));
        await db.SaveChangesAsync();

        var audit = () =>
            AuditArchive(
                db,
                "13F-HR\toriginal\t19-AUG-2026\t30-JUN-2026\t1948780\n",
                "original\tN\t\tMerged source manager\n",
                "original\t438516205\tSH\t\t264131\noriginal\t438516106\tSH\t\t4569\n"
            );
        if (duplicateObservation)
            await audit
                .Should()
                .ThrowAsync<InvalidDataException>()
                .WithMessage("*ambiguous retained security identities*");
        else
            await audit();
        (await db.Set<HoldingsImportFailure>().CountAsync()).Should().Be(expectedFailures);
        (await db.Set<InstitutionalHolding>().FirstAsync())
            .Shares.Should()
            .Be(partial ? 264131 : 268700);
    }

    [Theory]
    [InlineData("NEW HOLDINGS", false, false, 0)]
    [InlineData("NEW HOLDINGS", true, false, 1)]
    [InlineData("NEW HOLDINGS", true, true, 0)]
    [InlineData("RESTATEMENT", true, false, 0)]
    public async Task Audit_Amendments_ReplaceOnlyTheirOwnPersistenceKeys(
        string amendmentType,
        bool differentSecurity,
        bool originalPresent,
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
        if (originalPresent)
        {
            var original = Position(issuer.Id, holder.Id, "438516106", null);
            original.Shares = 4569;
            db.Add(original);
        }
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

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 0)]
    public async Task Audit_RequiresPositionProvenance_WithoutUndoingCurrentSourceQuantityRepairs(
        bool currentSource,
        bool sameShares,
        int expectedFailures
    )
    {
        await using var db = fixture.CreateDbContext();
        var issuer = new EquityIssuer { Name = "Restated issuer" };
        var holder = new InstitutionalHolder { Cik = "1948780", Name = "Restating manager" };
        db.AddRange(issuer, holder);
        db.Add(new EquityIssuerCusipAlias { EquityIssuerId = issuer.Id, Cusip = "11135F101" });
        var position = Position(issuer.Id, holder.Id, "11135F101", null);
        position.AccessionNumber = currentSource ? "0000000123-26-000002" : "0000000123-26-000001";
        position.FilingDate = new DateOnly(2026, 8, currentSource ? 20 : 19);
        position.Shares = sameShares ? 9256 : 460421;
        db.Add(position);
        // Import metadata may already describe the amendment even when its positions stayed old.
        db.Add(
            new InstitutionalFiling
            {
                InstitutionalHolderId = holder.Id,
                AccessionNumber = "0000000123-26-000002",
                ReportDate = position.ReportDate,
                FilingDate = new DateOnly(2026, 8, 20),
                FilingType = FilingType.Form13F,
                IsAmendment = true,
            }
        );
        await db.SaveChangesAsync();

        await AuditArchive(
            db,
            "13F-HR/A\t0000000123-26-000002\t20-AUG-2026\t30-JUN-2026\t1948780\n",
            "0000000123-26-000002\tY\tRESTATEMENT\tRestating manager\n",
            "0000000123-26-000002\t11135F101\tSH\t\t4600\n0000000123-26-000002\t11135F101\tSH\t\t4656\n"
        );

        var failures = await db.Set<HoldingsImportFailure>().ToListAsync();
        failures.Should().HaveCount(expectedFailures);
        if (expectedFailures > 0)
            failures.Single().AccessionNumber.Should().Be("0000000123-26-000002");
        (await db.Set<InstitutionalHolding>().SingleAsync()).Shares.Should().Be(position.Shares);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Audit_LaterAccessionOnSameDay_DoesNotRestoreOldSourcePositions(
        bool rollupOnly
    )
    {
        await using var db = fixture.CreateDbContext();
        var issuer = new EquityIssuer { Name = "Later issuer" };
        var holder = new InstitutionalHolder { Cik = "1948780", Name = "Later manager" };
        db.AddRange(issuer, holder);
        db.Add(new EquityIssuerCusipAlias { EquityIssuerId = issuer.Id, Cusip = "11135F101" });
        if (rollupOnly)
            db.Add(
                new InstitutionalFiling
                {
                    InstitutionalHolderId = holder.Id,
                    AccessionNumber = "0000000123-26-000002",
                    ReportDate = new DateOnly(2026, 6, 30),
                    FilingDate = new DateOnly(2026, 8, 19),
                    FilingType = FilingType.Form13F,
                    IsAmendment = true,
                }
            );
        else
        {
            var position = Position(issuer.Id, holder.Id, "999999999", null);
            position.AccessionNumber = "0000000123-26-000002";
            db.Add(position);
        }
        await db.SaveChangesAsync();

        await AuditArchive(
            db,
            "13F-HR\t0000000123-26-000001\t19-AUG-2026\t30-JUN-2026\t1948780\n",
            "0000000123-26-000001\tN\t\tLater manager\n",
            "0000000123-26-000001\t11135F101\tSH\t\t9256\n"
        );

        (await db.Set<HoldingsImportFailure>().CountAsync()).Should().Be(0);
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
