using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

[Collection(ParadeDbCollection.Name)]
public class DocumentRepositoryAsFiledScopeTests : ParadeDbMcpTestBase
{
    public DocumentRepositoryAsFiledScopeTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetPendingAsFiledHtml_SelectsPeriodicReportsWithTheExistingRetryAndOwnershipGuards()
    {
        var issuer = new EquityIssuer
        {
            Id = Guid.NewGuid(),
            Name = "Report issuer",
            Cik = "0000001234",
        };
        var unknownIssuer = new EquityIssuer
        {
            Id = Guid.NewGuid(),
            Name = "Unknown issuer",
            Cik = "",
        };
        DocumentType[] supported =
        [
            DocumentType.EightK,
            DocumentType.EightKa,
            DocumentType.TenK,
            DocumentType.TenKa,
            DocumentType.TenQ,
            DocumentType.TenQa,
            DocumentType.TwentyF,
            DocumentType.TwentyFa,
            DocumentType.FortyF,
            DocumentType.FortyFa,
            DocumentType.SixK,
            DocumentType.SixKa,
        ];
        var expected = supported
            .Select((type, index) => NewDocument(issuer, type, index + 1))
            .ToArray();
        var completed = NewDocument(issuer, DocumentType.TenK, 20);
        completed.AsFiledHtmlVersion = Document.AsFiledHtmlBuilderVersion;
        var parked = NewDocument(issuer, DocumentType.TenQ, 21);
        parked.AsFiledHtmlAttempts = Document.MaxAsFiledHtmlAttempts;
        var missingAccession = NewDocument(issuer, DocumentType.TwentyF, 22);
        missingAccession.AccessionNumber = null;
        var recoverableAccession = NewDocument(issuer, DocumentType.FortyF, 23);
        recoverableAccession.AccessionNumber = null;
        recoverableAccession.SourceUrl =
            "https://www.sec.gov/Archives/edgar/data/1234/0000001234-26-000023.txt";
        DbContext.AddRange(issuer, unknownIssuer);
        DbContext.AddRange(expected);
        DbContext.AddRange(
            completed,
            parked,
            missingAccession,
            recoverableAccession,
            NewDocument(unknownIssuer, DocumentType.TenK, 24),
            NewDocument(issuer, DocumentType.FormFour, 25),
            NewDocument(issuer, DocumentType.EsefAnnualReport, 26)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var actual = await new DocumentRepository(DbContext)
            .GetPendingAsFiledHtml()
            .Select(document => document.Id)
            .ToListAsync();

        actual
            .Should()
            .BeEquivalentTo(
                expected.Select(document => document.Id).Append(recoverableAccession.Id)
            );
    }

    private static Document NewDocument(EquityIssuer issuer, DocumentType type, int sequence) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = issuer.Id,
            Content = new Equibles.Media.Data.Models.File
            {
                Name = "report",
                Extension = "txt",
                ContentType = "text/plain",
                Size = 1,
                FileContent = new Equibles.Media.Data.Models.FileContent { Bytes = [0x01] },
            },
            DocumentType = type,
            ReportingDate = new DateOnly(2015, 6, 1),
            AccessionNumber = $"0000001234-26-{sequence:000000}",
            AsFiledHtmlVersion = 0,
        };
}
