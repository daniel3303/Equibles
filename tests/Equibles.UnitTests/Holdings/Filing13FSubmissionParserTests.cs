using Equibles.Holdings.HostedService.Services;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class Filing13FSubmissionParserTests
{
    private static readonly EdgarDailyIndexEntry Entry = new()
    {
        Cik = "1234",
        AccessionNumber = "0000001234-26-000001",
        DateFiled = new DateOnly(2026, 8, 14),
        FormType = "13F-HR",
    };

    private const string Submission = """
        <SEC-DOCUMENT>
        <DOCUMENT>
        <TYPE>13F-HR
        <SEQUENCE>1
        <FILENAME>primary_doc.xml
        <TEXT>
        <XML>
        <?xml version="1.0"?>
        <edgarSubmission>
          <headerData><cik>0000001234</cik></headerData>
          <formData>
            <coverPage>
              <reportCalendarOrQuarter>06-30-2026</reportCalendarOrQuarter>
              <isAmendment>false</isAmendment>
              <filingManager><name>Example Manager</name></filingManager>
            </coverPage>
            <summaryPage><tableEntryTotal>2</tableEntryTotal><tableValueTotal>300</tableValueTotal></summaryPage>
          </formData>
        </edgarSubmission>
        </XML>
        </TEXT>
        </DOCUMENT>
        <DOCUMENT>
        <TYPE>INFORMATION TABLE
        <SEQUENCE>2
        <FILENAME>positions.xml
        <TEXT>
        <XML>
        <?xml version="1.0"?>
        <informationTable>
          <infoTable>
            <cusip>111111111</cusip><value>100</value>
            <shrsOrPrnAmt><sshPrnamt>10</sshPrnamt><sshPrnamtType>SH</sshPrnamtType></shrsOrPrnAmt>
            <otherManager>1,2</otherManager><investmentDiscretion>DFND</investmentDiscretion>
            <votingAuthority><Sole>8</Sole><Shared>1</Shared><None>1</None></votingAuthority>
          </infoTable>
          <infoTable>
            <cusip>222222222</cusip><value>200</value>
            <shrsOrPrnAmt><sshPrnamt>20</sshPrnamt><sshPrnamtType>PRN</sshPrnamtType></shrsOrPrnAmt>
            <putCall>PUT</putCall>
          </infoTable>
        </informationTable>
        </XML>
        </TEXT>
        </DOCUMENT>
        </SEC-DOCUMENT>
        """;

    [Fact]
    public void CompleteSubmission_PreservesFiledRowsAndAttribution()
    {
        var filing = Filing13FSubmissionParser.Parse(Submission, Entry, new());

        filing.Should().NotBeNull();
        filing.PeriodOfReport.Should().Be(new DateOnly(2026, 6, 30));
        filing.AccessionNumber.Should().Be(Entry.AccessionNumber);
        filing.Holdings.Select(h => h.Cusip).Should().Equal("111111111", "222222222");
        filing.Holdings.Select(h => h.Shares).Should().Equal(10, 20);
        filing.Holdings[0].OtherManagers.Should().Be("1,2");
        filing.Holdings[0].VotingAuthSole.Should().Be(8);
        filing.Holdings[0].VotingAuthShared.Should().Be(1);
        filing.Holdings[0].VotingAuthNone.Should().Be(1);
        filing.Holdings[1].ShareType.Should().Be("PRN");
        filing.Holdings[1].PutCall.Should().Be("PUT");
    }

    [Theory]
    [InlineData("</SEC-DOCUMENT>", "")]
    [InlineData("<tableEntryTotal>2", "<tableEntryTotal>3")]
    [InlineData("<tableValueTotal>300", "<tableValueTotal>301")]
    [InlineData("<cik>0000001234", "<cik>9999")]
    [InlineData("<cik>0000001234</cik>", "")]
    [InlineData("<TYPE>13F-HR", "<TYPE>13F-HR/A")]
    [InlineData("<TYPE>INFORMATION TABLE", "<TYPE>EX-99")]
    [InlineData("<isAmendment>false", "<isAmendment>true")]
    [InlineData("<tableEntryTotal>2</tableEntryTotal>", "")]
    public void IncompleteOrInconsistentSubmission_IsNotAccepted(string oldValue, string newValue)
    {
        Filing13FSubmissionParser
            .Parse(Submission.Replace(oldValue, newValue), Entry, new())
            .Should()
            .BeNull();
    }

    [Fact]
    public void MultipleInformationTables_IncludeEveryDeclaredPosition()
    {
        var split = Submission.Replace(
            "</infoTable>\n  <infoTable>",
            """
            </infoTable></informationTable></XML></TEXT></DOCUMENT>
            <DOCUMENT>
            <TYPE>INFORMATION TABLE
            <FILENAME>second.xml
            <TEXT><XML><informationTable><infoTable>
            """
        );
        split.Should().Contain("second.xml");

        Filing13FSubmissionParser.Parse(split, Entry, new()).Holdings.Should().HaveCount(2);
    }

    [Fact]
    public void ExplicitlyEmptyAmendment_IsAcceptedWithoutAnInformationTable()
    {
        var coverEnd =
            Submission.IndexOf("</DOCUMENT>", StringComparison.Ordinal) + "</DOCUMENT>".Length;
        var empty = (Submission[..coverEnd] + "\n</SEC-DOCUMENT>")
            .Replace("<TYPE>13F-HR", "<TYPE>13F-HR/A")
            .Replace("<isAmendment>false", "<isAmendment>true")
            .Replace("<tableEntryTotal>2", "<tableEntryTotal>0")
            .Replace("<tableValueTotal>300", "<tableValueTotal>0");
        var amendment = new EdgarDailyIndexEntry
        {
            Cik = Entry.Cik,
            AccessionNumber = Entry.AccessionNumber,
            DateFiled = Entry.DateFiled,
            FormType = "13F-HR/A",
        };

        var filing = Filing13FSubmissionParser.Parse(empty, amendment, new());

        filing.Should().NotBeNull();
        filing.IsAmendment.Should().BeTrue();
        filing.Holdings.Should().BeEmpty();
    }

    [Fact]
    public async Task CallerCancellation_DoesNotFallBackToArtifactRequests()
    {
        using var cancellation = new CancellationTokenSource();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDocumentContent(Entry.AccessionNumber, Entry.Cik, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<string>(cancellation.Token);
            });
        var ingestion = new Realtime13FIngestionService(
            edgar,
            new Filing13FXmlParser(),
            null,
            null,
            null,
            NullLogger<Realtime13FIngestionService>.Instance
        );

        var act = () =>
            ingestion.IngestSpecificFilings([Entry], DateOnly.MinValue, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await edgar
            .DidNotReceive()
            .GetFilingArtifactNames(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RecordedSecSubmission_PreservesBothCusipsAndOtherManagerLists()
    {
        var source = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Holdings",
                "13f-complete-submission.txt"
            )
        );
        var entry = new EdgarDailyIndexEntry
        {
            Cik = "1053906",
            AccessionNumber = "0001053906-23-000008",
            DateFiled = new DateOnly(2023, 8, 11),
            FormType = "13F-HR",
        };

        var filing = Filing13FSubmissionParser.Parse(source, entry, new());

        filing.Should().NotBeNull();
        filing.PeriodOfReport.Should().Be(new DateOnly(2023, 6, 30));
        filing.Holdings.Should().HaveCount(119);
        filing.Holdings.Sum(h => h.Value).Should().Be(3_226_843);
        var current = filing.Holdings.Single(h => h.Cusip == "23255M204");
        var predecessor = filing.Holdings.Single(h => h.Cusip == "23255M105");
        current.Shares.Should().Be(138_403);
        current.OtherManagers.Should().Be("1,2,3,4,5");
        predecessor.Shares.Should().Be(552);
        predecessor.OtherManagers.Should().Be("1,2,3,4");
        filing.OtherManagers.Should().ContainKeys(1, 2, 3, 4, 5);
    }
}
