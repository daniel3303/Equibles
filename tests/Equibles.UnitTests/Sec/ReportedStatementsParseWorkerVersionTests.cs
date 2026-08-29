using Equibles.Media.BusinessLogic;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using NSubstitute;
using MediaFile = Equibles.Media.Data.Models.File;

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

    [Fact]
    public void RequeueMissingBundle_RetiresStaleFileAndReopensCapture()
    {
        var file = new MediaFile
        {
            StorageProvider = StorageProvider.FileSystem,
            RelativePath = "blob/sha256/aa/bb/missing",
            ContentHash = "sha256:missing",
        };
        var document = new Document
        {
            ReportedStatementsContent = file,
            ReportedStatementsContentId = file.Id,
            ReportedStatementsUncompressedSize = 123,
            ReportedStatementsStatus = XbrlCaptureStatus.Captured,
            ReportedStatementsCaptureAttempts = Document.MaxReportedStatementsCaptureAttempts,
            ReportedStatementsParseAttempts = Document.MaxReportedStatementsParseAttempts,
        };
        var fileManager = Substitute.For<IFileManager>();
        var service = new ReportedStatementsParseService(null!, fileManager);

        service.RequeueMissingBundle(document);

        fileManager.Received(1).DeleteFile(file);
        document.ReportedStatementsContent.Should().BeNull();
        document.ReportedStatementsContentId.Should().BeNull();
        document.ReportedStatementsUncompressedSize.Should().BeNull();
        document.ReportedStatementsStatus.Should().Be(XbrlCaptureStatus.NotChecked);
        document.ReportedStatementsCaptureAttempts.Should().Be(0);
        document.ReportedStatementsParseAttempts.Should().Be(0);
    }
}
