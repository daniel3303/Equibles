using System.Net;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class BeneficialOwnershipDetailSyncServiceTests : ParadeDbMcpTestBase
{
    private const string MorganStanley13G = "0000895421-26-000285";
    private const string AyurMaya13D = "0000902664-25-002531";

    private readonly ISecEdgarClient _edgar = Substitute.For<ISecEdgarClient>();

    public BeneficialOwnershipDetailSyncServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncMissing_StoresEveryStoredFilingsDetailsAndSkipsForm13F()
    {
        await SeedFiling(
            MorganStanley13G,
            "895421",
            FilingType.Schedule13G,
            new DateOnly(2026, 10, 9)
        );
        await SeedFiling(AyurMaya13D, "1975990", FilingType.Schedule13D, new DateOnly(2025, 5, 23));
        await SeedFiling(
            "0000895421-26-000001",
            "895421",
            FilingType.Form13F,
            new DateOnly(2026, 8, 14)
        );
        Serve(MorganStanley13G, "sc13ga-morgan-stanley-x0202.xml");
        Serve(AyurMaya13D, "sc13d-multiple-type-codes.xml");

        var more = await Sut().SyncMissing(10, CancellationToken.None);

        more.Should().BeFalse();
        await using var verify = Fixture.CreateDbContext();
        var details = await verify
            .Set<BeneficialOwnershipFiling>()
            .Include(f => f.ReportingPersons)
            .OrderBy(f => f.FilingDate)
            .ToListAsync();
        details.Select(f => f.AccessionNumber).Should().Equal(AyurMaya13D, MorganStanley13G);
        var thirteenG = details[1];
        thirteenG.Rules.Should().Be(Schedule13GRules.QualifiedInstitution);
        thirteenG.IsAmendment.Should().BeTrue();
        thirteenG
            .ReportingPersons.OrderBy(p => p.Ordinal)
            .Select(p => p.Types)
            .Should()
            .Equal(
                ReportingPersonTypes.ParentHoldingCompany | ReportingPersonTypes.Corporation,
                ReportingPersonTypes.BrokerDealer
                    | ReportingPersonTypes.InvestmentAdviser
                    | ReportingPersonTypes.Corporation
            );
        details[0]
            .ReportingPersons.Should()
            .ContainSingle(p => p.IsLead)
            .Which.Cik.Should()
            .Be("1975990");
        await _edgar
            .DidNotReceive()
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                "0000895421-26-000001",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SyncMissing_ReadsNewestFirstAndReportsWhatRemains()
    {
        await SeedFiling(
            MorganStanley13G,
            "895421",
            FilingType.Schedule13G,
            new DateOnly(2026, 10, 9)
        );
        await SeedFiling(AyurMaya13D, "1975990", FilingType.Schedule13D, new DateOnly(2025, 5, 23));
        Serve(MorganStanley13G, "sc13ga-morgan-stanley-x0202.xml");
        Serve(AyurMaya13D, "sc13d-multiple-type-codes.xml");

        (await Sut().SyncMissing(1, CancellationToken.None)).Should().BeTrue();
        (await Accessions()).Should().Equal(MorganStanley13G);

        (await Sut().SyncMissing(1, CancellationToken.None)).Should().BeFalse();
        (await Accessions()).Should().BeEquivalentTo([MorganStanley13G, AyurMaya13D]);
    }

    [Fact]
    public async Task SyncMissing_EdgarOutage_StopsWithoutMarkingAnything()
    {
        await SeedFiling(
            MorganStanley13G,
            "895421",
            FilingType.Schedule13G,
            new DateOnly(2026, 10, 9)
        );
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                MorganStanley13G,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(
                new HttpRequestException(
                    "Service Unavailable",
                    null,
                    HttpStatusCode.ServiceUnavailable
                )
            );

        (await Sut().SyncMissing(10, CancellationToken.None)).Should().BeTrue();

        (await Accessions()).Should().BeEmpty();
    }

    [Fact]
    public async Task SyncMissing_MissingOrUnparseableDocument_IsMarkedOnceAndNotRefetched()
    {
        await SeedFiling(
            MorganStanley13G,
            "895421",
            FilingType.Schedule13G,
            new DateOnly(2026, 10, 9)
        );
        await SeedFiling(AyurMaya13D, "1975990", FilingType.Schedule13D, new DateOnly(2025, 5, 23));
        // EDGAR answers a 404 with no bytes; the other document is not XML.
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                MorganStanley13G,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns([]);
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                AyurMaya13D,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("<html>not a schedule</html"u8.ToArray());

        await Sut().SyncMissing(10, CancellationToken.None);
        await Sut().SyncMissing(10, CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var details = await verify
            .Set<BeneficialOwnershipFiling>()
            .Include(f => f.ReportingPersons)
            .ToListAsync();
        details
            .Should()
            .HaveCount(2)
            .And.OnlyContain(f => f.IsUnreadable && f.ReportingPersons.Count == 0);
        await _edgar
            .Received(2)
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Write_SameAccessionTwice_KeepsOnlyTheLatestPersons()
    {
        var writer = Writer();
        var parser = new Filing13DGXmlParser();
        var filing = parser.ParseFiling(
            Asset("sc13ga-morgan-stanley-x0202.xml"),
            MorganStanley13G,
            "0000895421",
            new DateOnly(2026, 10, 9)
        );

        await writer.Write(filing, CancellationToken.None);
        filing.ReportingPersons.RemoveAt(1);
        await writer.Write(filing, CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<BeneficialOwnershipFiling>().CountAsync()).Should().Be(1);
        (await verify.Set<BeneficialOwnershipReportingPerson>().Select(p => p.Name).ToListAsync())
            .Should()
            .Equal("Morgan Stanley");
    }

    private void Serve(string accession, string asset) =>
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                accession,
                "primary_doc.xml",
                Arg.Any<CancellationToken>()
            )
            .Returns(System.Text.Encoding.UTF8.GetBytes(Asset(asset)));

    private static string Asset(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Holdings13DG", name)
        );

    private async Task<List<string>> Accessions()
    {
        await using var verify = Fixture.CreateDbContext();
        return await verify
            .Set<BeneficialOwnershipFiling>()
            .Select(f => f.AccessionNumber)
            .ToListAsync();
    }

    private async Task SeedFiling(
        string accession,
        string cik,
        FilingType type,
        DateOnly filingDate
    )
    {
        var holder =
            await DbContext.Set<InstitutionalHolder>().FirstOrDefaultAsync(h => h.Cik == cik)
            ?? new InstitutionalHolder { Cik = cik, Name = "Holder " + cik };
        DbContext.Add(
            new InstitutionalFiling
            {
                AccessionNumber = accession,
                InstitutionalHolder = holder,
                FilingDate = filingDate,
                ReportDate = filingDate,
                FilingType = type,
            }
        );
        await DbContext.SaveChangesAsync();
    }

    private BeneficialOwnershipDetailWriter Writer() =>
        new(
            ServiceScopeSubstitute.Create(
                (typeof(EquiblesFinancialDbContext), Fixture.CreateDbContext())
            ),
            NullLogger<BeneficialOwnershipDetailWriter>()
        );

    private BeneficialOwnershipDetailSyncService Sut()
    {
        var context = Fixture.CreateDbContext();
        return new BeneficialOwnershipDetailSyncService(
            context,
            _edgar,
            new Filing13DGXmlParser(),
            Writer(),
            NullLogger<BeneficialOwnershipDetailSyncService>()
        );
    }
}
