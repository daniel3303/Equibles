using System.Text;
using Equibles.Holdings.HostedService.Services;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class Realtime13FSubmissionFallbackTests
{
    private static readonly EdgarDailyIndexEntry Entry = new()
    {
        Cik = "1162777",
        AccessionNumber = "0000950123-20-012501",
        DateFiled = new DateOnly(2020, 12, 7),
        FormType = "13F-HR",
    };

    private static string Source() =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Holdings",
                "13f-missing-table-artifact-submission.txt"
            )
        );

    [Fact]
    public async Task MissingStandaloneTable_PreservesOriginalRowsWithoutVerifyingCoverTotals()
    {
        var (service, edgar) = CreateService();

        var filing = await service.ParseFiling(Entry, CancellationToken.None);

        filing.Should().NotBeNull();
        filing.CompleteSubmissionVerified.Should().BeFalse();
        filing.TableEntryTotal.Should().Be(8);
        filing.TableValueTotal.Should().Be(4_359_220);
        filing.Holdings.Should().HaveCount(10);
        filing.Holdings.Sum(h => h.Value).Should().Be(4_406_898);
        filing.Holdings.Single(h => h.Cusip == "464287242").Shares.Should().Be(36_515);
        Realtime13FIngestionService.IsSourceConfirmedZeroOriginal(filing).Should().BeFalse();
        await edgar
            .Received(1)
            .GetDocumentFileBytes(
                Entry.Cik,
                Entry.AccessionNumber,
                "1972.xml",
                Arg.Any<CancellationToken>()
            );
        await edgar
            .DidNotReceive()
            .GetFilingArtifactNames(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AvailableStandaloneTable_RetainsPrecedenceOverUnreconciledSubmission()
    {
        const string standalone = """
            <informationTable><infoTable><cusip>464287242</cusip><value>4919</value>
            <shrsOrPrnAmt><sshPrnamt>36515</sshPrnamt><sshPrnamtType>SH</sshPrnamtType></shrsOrPrnAmt>
            </infoTable></informationTable>
            """;
        var (service, edgar) = CreateService();
        edgar
            .GetDocumentFileBytes(
                Entry.Cik,
                Entry.AccessionNumber,
                "1972.xml",
                Arg.Any<CancellationToken>()
            )
            .Returns(Encoding.UTF8.GetBytes(standalone));

        var filing = await service.ParseFiling(Entry, CancellationToken.None);

        filing.Holdings.Should().ContainSingle();
        filing.Holdings[0].Shares.Should().Be(36_515);
        filing.CompleteSubmissionVerified.Should().BeFalse();
    }

    [Fact]
    public void UnreconciledAmendment_DoesNotOfferAnOriginalFallback()
    {
        var source = Source()
            .Replace("<TYPE>13F-HR", "<TYPE>13F-HR/A")
            .Replace("<isAmendment>false", "<isAmendment>true");
        var entry = new EdgarDailyIndexEntry
        {
            Cik = Entry.Cik,
            AccessionNumber = Entry.AccessionNumber,
            DateFiled = Entry.DateFiled,
            FormType = "13F-HR/A",
        };

        var result = Filing13FSubmissionParser.Parse(source, entry, new());

        result.Filing.Should().BeNull();
        result.OriginalFallback.Should().BeNull();
    }

    [Theory]
    [InlineData("</SEC-DOCUMENT>", "")]
    [InlineData("<cik>0001162777", "<cik>0000000001")]
    [InlineData("<FILENAME>1972.xml", "<FILENAME>../1972.xml")]
    public void InvalidEnvelope_DoesNotOfferAFallback(string before, string after)
    {
        var source = Source();
        source.Should().Contain(before);

        Filing13FSubmissionParser
            .Parse(source.Replace(before, after), Entry, new())
            .Should()
            .BeNull();
    }

    [Fact]
    public void ZeroQuantitiesWithUnreconciledTotals_DoNotOfferAFallback()
    {
        var source = Source();
        var amounts = System.Text.RegularExpressions.Regex.Matches(
            source,
            @"<sshPrnamt>\d+</sshPrnamt>"
        );
        foreach (System.Text.RegularExpressions.Match amount in amounts)
            source = source.Replace(amount.Value, "<sshPrnamt>0</sshPrnamt>");

        var result = Filing13FSubmissionParser.Parse(source, Entry, new());

        result.Filing.Should().BeNull();
        result.OriginalFallback.Should().BeNull();
    }

    private static (Realtime13FIngestionService Service, ISecEdgarClient Edgar) CreateService()
    {
        var source = Source();
        var coverStart = source.IndexOf("<edgarSubmission", StringComparison.Ordinal);
        var coverEnd =
            source.IndexOf("</edgarSubmission>", StringComparison.Ordinal)
            + "</edgarSubmission>".Length;
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDocumentContent(Entry.AccessionNumber, Entry.Cik, Arg.Any<CancellationToken>())
            .Returns(source);
        edgar
            .GetDocumentFileBytes(
                Entry.Cik,
                Entry.AccessionNumber,
                "primary_doc.xml",
                Arg.Any<CancellationToken>()
            )
            .Returns(Encoding.UTF8.GetBytes(source[coverStart..coverEnd]));
        edgar
            .GetDocumentFileBytes(
                Entry.Cik,
                Entry.AccessionNumber,
                "1972.xml",
                Arg.Any<CancellationToken>()
            )
            .Returns(Array.Empty<byte>());
        return (
            new Realtime13FIngestionService(
                edgar,
                new(),
                null,
                null,
                null,
                NullLogger<Realtime13FIngestionService>.Instance
            ),
            edgar
        );
    }
}
