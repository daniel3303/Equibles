using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class ReportedStatementsParseWorkerVersionTests
{
    [Fact]
    public void NeedsCurrentParser_OldAttemptBudgetReopensAfterVersionUpgrade()
    {
        var document = new Document
        {
            ReportedStatementsParseVersion = Document.ReportedStatementsParserVersion - 1,
            ReportedStatementsParseAttemptVersion = Document.ReportedStatementsParserVersion - 1,
            ReportedStatementsParseAttempts = Document.MaxReportedStatementsParseAttempts,
        };

        ReportedStatementsParseWorker.NeedsCurrentParser.Compile()(document).Should().BeTrue();
        ReportedStatementsParseWorker.BeginCurrentParserAttempt(document);

        document
            .ReportedStatementsParseAttemptVersion.Should()
            .Be(Document.ReportedStatementsParserVersion);
        document.ReportedStatementsParseAttempts.Should().Be(0);
    }

    [Fact]
    public void NeedsCurrentParser_CurrentAttemptBudgetRemainsCapped()
    {
        var document = new Document
        {
            ReportedStatementsParseVersion = Document.ReportedStatementsParserVersion - 1,
            ReportedStatementsParseAttemptVersion = Document.ReportedStatementsParserVersion,
            ReportedStatementsParseAttempts = Document.MaxReportedStatementsParseAttempts,
        };

        ReportedStatementsParseWorker.NeedsCurrentParser.Compile()(document).Should().BeFalse();
    }

    [Fact]
    public void SuccessfulReplayStateResetsAttemptsAndClosesTheWorkset()
    {
        var document = new Document
        {
            ReportedStatementsParseVersion = Document.ReportedStatementsParserVersion - 1,
            ReportedStatementsParseAttemptVersion = Document.ReportedStatementsParserVersion,
            ReportedStatementsParseAttempts = 3,
        };

        ReportedStatementsParseWorker.CompleteCurrentParserAttempt(document);

        ReportedStatementsParseWorker.NeedsCurrentParser.Compile()(document).Should().BeFalse();
        document.ReportedStatementsParseAttempts.Should().Be(0);
    }

    [Fact]
    public async Task MissingCapturedContentCannotStampTheCurrentParserVersion()
    {
        var oldVersion = Document.ReportedStatementsParserVersion - 1;
        var document = new Document
        {
            ReportedStatementsParseVersion = oldVersion,
            ReportedStatementsParseAttemptVersion = Document.ReportedStatementsParserVersion,
        };
        var service = new ReportedStatementsParseService(null!, null!);

        Func<Task> act = () => service.Parse(document, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        document.ReportedStatementsParseVersion.Should().Be(oldVersion);
    }
}
