using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Holdings;

public class Filing13FZeroPositionEvidenceTests
{
    private static readonly EdgarDailyIndexEntry Entry = new()
    {
        Cik = "1729347",
        AccessionNumber = "0001999371-26-020307",
        DateFiled = new DateOnly(2026, 9, 10),
        FormType = "13F-HR/A",
    };

    private static Parsed13FFiling Parse(string oldValue = null, string newValue = null)
    {
        var source = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Holdings",
                "13f-zero-restatement-submission.txt"
            )
        );
        if (oldValue != null)
        {
            source.Should().Contain(oldValue);
            source = source.Replace(oldValue, newValue);
        }
        return Filing13FSubmissionParser.Parse(source, Entry, new())?.Filing;
    }

    [Fact]
    public void RecordedRestatement_VerifiesAnEmptyBookWithoutClassifyingItsIdentifier()
    {
        var filing = Parse();
        filing.CompleteSubmissionVerified.Should().BeTrue();
        filing.TableEntryTotal.Should().Be(1);
        filing.TableValueTotal.Should().Be(0);
        Filing13FZeroPositionEvidence.IsRestatement(filing).Should().BeTrue();
        Filing13FZeroPositionEvidence.IsOriginal(filing).Should().BeFalse();
        filing.Holdings[0].Cusip = "037833100";
        Filing13FZeroPositionEvidence.IsRestatement(filing).Should().BeTrue();
    }

    [Theory]
    [InlineData("RESTATEMENT", "NEW HOLDINGS")]
    [InlineData("<amendmentType>RESTATEMENT</amendmentType>", "")]
    [InlineData("<reportType>13F HOLDINGS REPORT", "<reportType>13F COMBINATION REPORT")]
    [InlineData("<otherIncludedManagersCount>0", "<otherIncludedManagersCount>1")]
    [InlineData("<sshPrnamt>0</sshPrnamt>", "")]
    [InlineData("<value>0</value>", "")]
    [InlineData("<Sole>0</Sole>", "")]
    [InlineData("<Shared>0</Shared>", "")]
    [InlineData("<None>0</None>", "")]
    [InlineData("<sshPrnamt>0", "<sshPrnamt>1")]
    [InlineData("<Shared>0", "<Shared>1")]
    [InlineData("<tableEntryTotal>1", "<tableEntryTotal>2")]
    [InlineData("<tableValueTotal>0", "<tableValueTotal>1")]
    [InlineData(
        "<summaryPage>",
        "<summaryPage><isConfidentialOmitted>true</isConfidentialOmitted>"
    )]
    [InlineData(
        "<coverPage>",
        "<coverPage><confidentialTreatmentRequestedFlag>true</confidentialTreatmentRequestedFlag>"
    )]
    [InlineData(
        "<investmentDiscretion>OTR",
        "<otherManager>1</otherManager><investmentDiscretion>OTR"
    )]
    [InlineData("</SEC-DOCUMENT>", "")]
    public void UnverifiedOrNonzeroSource_DoesNotAuthorizeRemoval(string oldValue, string newValue)
    {
        var filing = Parse(oldValue, newValue);
        (filing != null && Filing13FZeroPositionEvidence.IsRestatement(filing)).Should().BeFalse();
    }

    [Fact]
    public void StandaloneOrOriginalEvidence_DoesNotAuthorizeRemoval()
    {
        var filing = Parse();
        filing.CompleteSubmissionVerified = false;
        Filing13FZeroPositionEvidence.IsRestatement(filing).Should().BeFalse();
        filing.CompleteSubmissionVerified = true;
        filing.IsAmendment = false;
        Filing13FZeroPositionEvidence.IsRestatement(filing).Should().BeFalse();
        Filing13FZeroPositionEvidence.IsOriginal(filing).Should().BeTrue();
    }
}
