using Equibles.CommonStocks.Data;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using MediaFile = Equibles.Media.Data.Models.File;

namespace Equibles.UnitTests.Sec;

public class ReportedStatementsParseWorkerVersionTests
{
    [Fact]
    public void ReplayTiers_PlaceEveryDocumentTypeInExactlyOneTier()
    {
        var types = typeof(DocumentType)
            .GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
            )
            .Where(field => field.FieldType == typeof(DocumentType))
            .Select(field => (DocumentType)field.GetValue(null))
            .Append(null)
            .ToList();
        var tiers = ReportedStatementsParseWorker
            .ReplayTiers.Select(tier => tier.Compile())
            .ToList();

        types
            .Should()
            .OnlyContain(type =>
                tiers.Count(tier => tier(new Document { DocumentType = type })) == 1
            );
    }

    [Fact]
    public void ReplayTiers_ReadForeignAnnualFormsThenSixKsThenTheRest()
    {
        var documents = new[]
        {
            new Document
            {
                DocumentType = DocumentType.TenQ,
                ReportingDate = new DateOnly(2026, 9, 30),
            },
            new Document
            {
                DocumentType = DocumentType.TwentyF,
                ReportingDate = new DateOnly(2018, 12, 31),
            },
            new Document
            {
                DocumentType = DocumentType.SixK,
                ReportingDate = new DateOnly(2026, 6, 30),
            },
            new Document
            {
                DocumentType = DocumentType.TenK,
                ReportingDate = new DateOnly(2025, 12, 31),
            },
            new Document
            {
                DocumentType = DocumentType.FortyFa,
                ReportingDate = new DateOnly(2024, 12, 31),
            },
        };

        ReportedStatementsParseWorker
            .ReplayTiers.SelectMany(tier =>
                documents
                    .AsQueryable()
                    .Where(tier)
                    .OrderByDescending(document => document.ReportingDate)
                    .Select(document => document.DocumentType)
            )
            .Should()
            .Equal(
                DocumentType.FortyFa,
                DocumentType.TwentyF,
                DocumentType.SixK,
                DocumentType.TenQ,
                DocumentType.TenK
            );
    }

    [Theory]
    [InlineData(3, new[] { "FortyFa", "TwentyF", "SixK" })]
    [InlineData(5, new[] { "FortyFa", "TwentyF", "SixK", "SixKa", "TenQ" })]
    [InlineData(10, new[] { "FortyFa", "TwentyF", "SixK", "SixKa", "TenQ", "TenK" })]
    public async Task NextReplayBatch_FillsTheBatchTierByTier(int batchSize, string[] expected)
    {
        await using var db = NewDb();
        db.Set<Document>()
            .AddRange(
                ReplayDocument(DocumentType.TenQ, 2026, 9, 30),
                ReplayDocument(DocumentType.TwentyF, 2018, 12, 31),
                ReplayDocument(DocumentType.SixK, 2026, 6, 30),
                ReplayDocument(DocumentType.TenK, 2025, 12, 31),
                ReplayDocument(DocumentType.FortyFa, 2024, 12, 31),
                ReplayDocument(DocumentType.SixKa, 2025, 12, 31)
            );
        await db.SaveChangesAsync();

        var batch = await ReportedStatementsParseWorker.NextReplayBatch(
            db.Set<Document>(),
            batchSize,
            CancellationToken.None
        );

        batch.Select(document => document.DocumentType.Value).Should().Equal(expected);
    }

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var db = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new DocumentOnlyModuleConfiguration(),
                new MediaModuleConfiguration(),
            }
        );
        db.Database.EnsureCreated();
        return db;
    }

    private static Document ReplayDocument(DocumentType type, int year, int month, int day) =>
        new() { DocumentType = type, ReportingDate = new DateOnly(year, month, day) };

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
