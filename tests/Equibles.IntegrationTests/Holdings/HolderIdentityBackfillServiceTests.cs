using System.Net;
using System.Text;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The one-off identity backfill reads each unstamped holder's newest 13F cover page once. It must
/// pick the newest report, never overwrite a holder an import stamped meanwhile, finish holders
/// whose cover page is unpublished, and leave transient failures for a later cycle.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HolderIdentityBackfillServiceTests : ParadeDbMcpTestBase
{
    private readonly ISecEdgarClient _edgar = Substitute.For<ISecEdgarClient>();

    public HolderIdentityBackfillServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task UnstampedHolder_TakesTheNumbersOfItsNewest13FCover()
    {
        var holder = await SeedHolder("102909", crd: null);
        await SeedFiling(holder, "0000102909-25-000001", new DateOnly(2025, 3, 31));
        await SeedFiling(holder, "0000102909-25-000002", new DateOnly(2025, 6, 30));
        await SeedFiling(
            holder,
            "0000102909-25-000003",
            new DateOnly(2025, 9, 30),
            FilingType.Schedule13G
        );
        CoverPage("102909", "0000102909-25-000002", crd: "000105958", secFileNumber: "801-11953");

        var stamped = await Sut().Run(100, CancellationToken.None);

        stamped.Should().Be(1);
        var stored = await Reload(holder.Id);
        stored.CrdNumber.Should().Be("000105958");
        stored.SecFileNumber.Should().Be("801-11953");
        stored.IdentityAccession.Should().Be("0000102909-25-000002");
        await _edgar
            .DidNotReceive()
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                "0000102909-25-000001",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task BlankCoverNumber_KeepsTheStoredOne()
    {
        var holder = await SeedHolder("1067983", crd: "000314159");
        await SeedFiling(holder, "0000950123-26-000001", new DateOnly(2026, 6, 30));
        CoverPage("1067983", "0000950123-26-000001", crd: null, secFileNumber: null);

        await Sut().Run(100, CancellationToken.None);

        var stored = await Reload(holder.Id);
        stored.CrdNumber.Should().Be("000314159");
        stored.IdentityAccession.Should().Be("0000950123-26-000001");
    }

    [Fact]
    public async Task HolderAnImportAlreadyStamped_IsNeitherReadNorOverwritten()
    {
        var holder = await SeedHolder(
            "1364742",
            crd: "000107105",
            identityAccession: "0001086364-26-000099"
        );
        await SeedFiling(holder, "0001086364-26-000001", new DateOnly(2026, 6, 30));

        var stamped = await Sut().Run(100, CancellationToken.None);

        stamped.Should().Be(0);
        (await Reload(holder.Id)).CrdNumber.Should().Be("000107105");
        await _edgar
            .DidNotReceive()
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task UnpublishedCoverPage_StampsTheHolderSoItIsNotFetchedAgain()
    {
        var holder = await SeedHolder("2012383", crd: null);
        await SeedFiling(holder, "0002012383-26-000001", new DateOnly(2026, 6, 30));
        _edgar
            .GetDocumentFileBytes(
                "2012383",
                "0002012383-26-000001",
                "primary_doc.xml",
                Arg.Any<CancellationToken>()
            )
            .Returns([]);

        await Sut().Run(100, CancellationToken.None);

        var stored = await Reload(holder.Id);
        stored.CrdNumber.Should().BeNull();
        stored.IdentityAccession.Should().Be("0002012383-26-000001");
    }

    [Fact]
    public async Task TransientFailure_LeavesTheHolderForALaterCycle()
    {
        var holder = await SeedHolder("1374170", crd: null);
        await SeedFiling(holder, "0001374170-26-000001", new DateOnly(2026, 6, 30));
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable));

        var stamped = await Sut().Run(100, CancellationToken.None);

        stamped.Should().Be(0);
        (await Reload(holder.Id)).IdentityAccession.Should().BeNull();
    }

    [Fact]
    public async Task SameQuarterFilings_TheLaterFiledCoverWins()
    {
        var holder = await SeedHolder("1067983", crd: null);
        await SeedFiling(
            holder,
            "0000950123-26-000001",
            new DateOnly(2026, 6, 30),
            filedDaysAfter: 45
        );
        await SeedFiling(
            holder,
            "0000950123-26-000002",
            new DateOnly(2026, 6, 30),
            filedDaysAfter: 60
        );
        CoverPage("1067983", "0000950123-26-000002", crd: "000314159", secFileNumber: null);

        await Sut().Run(100, CancellationToken.None);

        (await Reload(holder.Id)).IdentityAccession.Should().Be("0000950123-26-000002");
    }

    [Fact]
    public async Task NewHoldingsAmendmentCover_FallsBackToTheFilingBeforeIt()
    {
        var holder = await SeedHolder("1067983", crd: null);
        await SeedFiling(
            holder,
            "0000950123-26-000001",
            new DateOnly(2026, 6, 30),
            filedDaysAfter: 45
        );
        await SeedFiling(
            holder,
            "0000950123-26-000002",
            new DateOnly(2026, 6, 30),
            filedDaysAfter: 90
        );
        CoverPage(
            "1067983",
            "0000950123-26-000002",
            crd: "000999999",
            secFileNumber: null,
            amendmentType: "NEW HOLDINGS"
        );
        CoverPage("1067983", "0000950123-26-000001", crd: "000314159", secFileNumber: null);

        await Sut().Run(100, CancellationToken.None);

        var stored = await Reload(holder.Id);
        stored.CrdNumber.Should().Be("000314159");
        stored.IdentityAccession.Should().Be("0000950123-26-000001");
    }

    [Fact]
    public async Task StoredParentNumber_TheCoverListsForAnotherManager_IsCleared()
    {
        var holder = await SeedHolder("895421", crd: "000000361");
        await SeedFiling(holder, "0000895421-26-000183", new DateOnly(2026, 3, 31));
        CoverPage(
            "895421",
            "0000895421-26-000183",
            crd: null,
            secFileNumber: null,
            coverManagersXml: "<otherManagersInfo><otherManager><cik>0002026079</cik>"
                + "<crdNumber>000000361</crdNumber><name>PARENT</name></otherManager></otherManagersInfo>"
        );

        await Sut().Run(100, CancellationToken.None);

        (await Reload(holder.Id)).CrdNumber.Should().BeNull();
    }

    [Fact]
    public async Task UnparseableCover_StampsAndKeepsTheStoredNumbers()
    {
        var holder = await SeedHolder("1067983", crd: "000314159");
        await SeedFiling(holder, "0000950123-26-000001", new DateOnly(2026, 6, 30));
        _edgar
            .GetDocumentFileBytes(
                "1067983",
                "0000950123-26-000001",
                "primary_doc.xml",
                Arg.Any<CancellationToken>()
            )
            .Returns(Encoding.UTF8.GetBytes("<html>not a cover"));

        await Sut().Run(100, CancellationToken.None);

        var stored = await Reload(holder.Id);
        stored.CrdNumber.Should().Be("000314159");
        stored.IdentityAccession.Should().Be("0000950123-26-000001");
    }

    [Fact]
    public async Task HolderWithoutACik_IsStampedWithoutAFetch()
    {
        var holder = await SeedHolder(" ", crd: null);
        await SeedFiling(holder, "0000000000-26-000001", new DateOnly(2026, 6, 30));

        await Sut().Run(100, CancellationToken.None);

        (await Reload(holder.Id)).IdentityAccession.Should().Be("0000000000-26-000001");
        await _edgar
            .DidNotReceive()
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Timeout_StopsTheRunWithoutThrowingAndLeavesTheRestForLater()
    {
        var first = await SeedHolder("1374170", crd: null);
        var second = await SeedHolder("1374171", crd: null);
        await SeedFiling(first, "0001374170-26-000001", new DateOnly(2026, 6, 30));
        await SeedFiling(second, "0001374171-26-000001", new DateOnly(2026, 6, 30));
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new TaskCanceledException("HttpClient.Timeout"));

        var stamped = await Sut().Run(100, CancellationToken.None);

        stamped.Should().Be(0);
        await _edgar
            .Received(1)
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ImportStampingTheHolderMidRun_IsNotOverwritten()
    {
        var holder = await SeedHolder("102909", crd: null);
        await SeedFiling(holder, "0000102909-25-000002", new DateOnly(2025, 6, 30));
        CoverPage("102909", "0000102909-25-000002", crd: "000999999", secFileNumber: null);
        // The import's refresh lands between the backfill's read and its write.
        _edgar
            .When(client =>
                client.GetDocumentFileBytes(
                    "102909",
                    "0000102909-25-000002",
                    "primary_doc.xml",
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(_ =>
            {
                using var import = Fixture.CreateDbContext();
                import
                    .Set<InstitutionalHolder>()
                    .Where(h => h.Id == holder.Id)
                    .ExecuteUpdate(set =>
                        set.SetProperty(h => h.CrdNumber, "000105958")
                            .SetProperty(h => h.IdentityAccession, "0000102909-25-000099")
                    );
            });

        var stamped = await Sut().Run(100, CancellationToken.None);

        stamped.Should().Be(0);
        var stored = await Reload(holder.Id);
        stored.CrdNumber.Should().Be("000105958");
        stored.IdentityAccession.Should().Be("0000102909-25-000099");
    }

    private HolderIdentityBackfillService Sut() =>
        new(
            _edgar,
            new Filing13FXmlParser(),
            new InstitutionalHolderRepository(DbContext),
            NullLogger<HolderIdentityBackfillService>()
        );

    private void CoverPage(
        string cik,
        string accession,
        string crd,
        string secFileNumber,
        string amendmentType = null,
        string coverManagersXml = ""
    )
    {
        var amendment =
            amendmentType == null
                ? ""
                : "<amendmentInfo><amendmentType>"
                    + amendmentType
                    + "</amendmentType></amendmentInfo>";
        var xml =
            "<edgarSubmission><headerData><cik>"
            + cik
            + "</cik></headerData><coverPage>"
            + "<reportCalendarOrQuarter>06-30-2025</reportCalendarOrQuarter>"
            + amendment
            + "<filingManager><name>FILER</name></filingManager>"
            + "<form13FFileNumber>028-01234</form13FFileNumber>"
            + (crd == null ? "" : "<crdNumber>" + crd + "</crdNumber>")
            + (secFileNumber == null ? "" : "<secFileNumber>" + secFileNumber + "</secFileNumber>")
            + coverManagersXml
            + "</coverPage></edgarSubmission>";
        _edgar
            .GetDocumentFileBytes(cik, accession, "primary_doc.xml", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes(xml));
    }

    private async Task<InstitutionalHolder> SeedHolder(
        string cik,
        string crd,
        string identityAccession = null
    )
    {
        var holder = new InstitutionalHolder
        {
            Cik = cik,
            Name = "Filer " + cik,
            CrdNumber = crd,
            IdentityAccession = identityAccession,
        };
        DbContext.Add(holder);
        await DbContext.SaveChangesAsync();
        return holder;
    }

    private async Task SeedFiling(
        InstitutionalHolder holder,
        string accession,
        DateOnly reportDate,
        FilingType filingType = FilingType.Form13F,
        int filedDaysAfter = 45
    )
    {
        DbContext.Add(
            new InstitutionalFiling
            {
                AccessionNumber = accession,
                InstitutionalHolderId = holder.Id,
                ReportDate = reportDate,
                FilingDate = reportDate.AddDays(filedDaysAfter),
                FilingType = filingType,
                PositionCount = 1,
                TotalValue = 1_000_000L,
            }
        );
        await DbContext.SaveChangesAsync();
    }

    private async Task<InstitutionalHolder> Reload(Guid holderId)
    {
        await using var verify = Fixture.CreateDbContext();
        return await verify
            .Set<InstitutionalHolder>()
            .AsNoTracking()
            .SingleAsync(h => h.Id == holderId);
    }
}
