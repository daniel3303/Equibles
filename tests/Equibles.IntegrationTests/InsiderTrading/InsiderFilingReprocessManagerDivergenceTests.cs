using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;
using FileContent = Equibles.Media.Data.Models.FileContent;

namespace Equibles.IntegrationTests.InsiderTrading;

/// <summary>
/// Pin for the row-count divergence path in <c>ReprocessFiling</c>. When the cached XML
/// re-parses to fewer transactions than the stored filing has rows, each stored row is
/// matched to a parsed row by <c>TransactionOrder</c>; a stored row with no match keeps
/// its prior <c>SecurityKind</c>/<c>Notes</c> but must still be advanced to the current
/// parser version, otherwise it would be re-selected on every future run.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerDivergenceTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerDivergenceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_CachedXmlReparsesFewerRowsThanStored_StampsDocumentIdentityOnEveryRow(
        bool isAmendment
    )
    {
        var date = new DateOnly(2024, 6, 14);
        var originalFilingDate = new DateOnly(2024, 5, 31);
        var accession = isAmendment ? "0000320193-24-000053" : "0000320193-24-000050";

        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "0001",
            Name = "Jane Insider",
            City = "Cupertino",
            StateOrCountry = "CA",
            IsDirector = true,
        };

        // Two stale rows. The cached XML below has a single transaction (order 0), so
        // order 0 re-parses and reclassifies while order 1 has no parsed counterpart.
        InsiderTransaction MakeStale(int order) =>
            new()
            {
                Id = Guid.NewGuid(),
                CommonStockId = stock.Id,
                InsiderOwnerId = owner.Id,
                AccessionNumber = accession,
                TransactionOrder = order,
                FilingDate = date,
                TransactionDate = date,
                TransactionCode = TransactionCode.Purchase,
                Shares = 1000,
                PricePerShare = 55m,
                ReportedPricePerShare = 55m,
                AcquiredDisposed = AcquiredDisposed.Acquired,
                SharesOwnedAfter = 5000,
                OwnershipNature = OwnershipNature.Direct,
                SecurityTitle = "Common Stock",
                SecurityKind = InsiderSecurityKind.Derivative,
                FilingForm = InsiderOwnershipForm.Unknown,
                ParserVersion = 0,
            };
        var matched = MakeStale(0);
        var unmatched = MakeStale(1);

        var documentType = isAmendment ? "5/A" : "5";
        var originalSubmission = isAmendment
            ? $"<dateOfOriginalSubmission>{originalFilingDate:yyyy-MM-dd}</dateOfOriginalSubmission>"
            : string.Empty;
        var ownershipXml =
            $"<ownershipDocument><documentType>{documentType}</documentType>{originalSubmission}"
            + "<nonDerivativeTable><nonDerivativeTransaction>"
            + "<securityTitle><value>Common Stock</value></securityTitle>"
            + "<transactionDate><value>2024-06-14</value></transactionDate>"
            + "<transactionCoding><transactionCode>P</transactionCode></transactionCoding>"
            + "<transactionAmounts>"
            + "<transactionShares><value>1000</value></transactionShares>"
            + "<transactionPricePerShare><value>55</value></transactionPricePerShare>"
            + "</transactionAmounts>"
            + "</nonDerivativeTransaction></nonDerivativeTable>"
            + "</ownershipDocument>";
        var rawBytes = Encoding.UTF8.GetBytes(ownershipXml);
        var filing = new InsiderFiling
        {
            AccessionNumber = accession,
            CaptureStatus = InsiderFilingCaptureStatus.Captured,
            UncompressedSize = rawBytes.Length,
            Content = new File
            {
                Name = accession,
                Extension = "gz",
                ContentType = "application/gzip",
                FileContent = new FileContent { Bytes = GzipCompressor.Compress(rawBytes) },
            },
        };

        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.Add(
            new DailyStockPrice
            {
                CommonStockId = stock.Id,
                Date = date,
                Close = 55m,
            }
        );
        DbContext.Add(matched);
        DbContext.Add(unmatched);
        DbContext.Add(filing);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var edgar = Substitute.For<ISecEdgarClient>();
        var fileManager = InsiderReprocessTestSupport.NewFileManager();

        await using var runCtx = Fixture.CreateDbContext();
        var manager = new InsiderFilingReprocessManager(
            new InsiderTransactionRepository(runCtx),
            new InsiderFilingRepository(runCtx),
            new DailyStockPriceRepository(runCtx),
            new StockSplitRepository(runCtx),
            new InsiderTransactionPriceValidator(),
            edgar,
            fileManager,
            runCtx,
            NullLogger<InsiderFilingReprocessManager>()
        );

        var result = await manager.Run();

        result.Failed.Should().Be(0);
        result.Processed.Should().Be(1);
        // Only the matched row flipped Derivative -> NonDerivative.
        result.Reclassified.Should().Be(1);
        // Served from the cache, no EDGAR round-trip.
        await edgar.DidNotReceive().GetDocumentContent(Arg.Any<string>(), Arg.Any<string>());

        await using var verify = Fixture.CreateDbContext();
        var matchedAfter = await verify.Set<InsiderTransaction>().FindAsync(matched.Id);
        var unmatchedAfter = await verify.Set<InsiderTransaction>().FindAsync(unmatched.Id);

        matchedAfter!.SecurityKind.Should().Be(InsiderSecurityKind.NonDerivative);
        matchedAfter.FilingForm.Should().Be(InsiderOwnershipForm.Form5);
        matchedAfter.IsAmendment.Should().Be(isAmendment);
        matchedAfter.OriginalFilingDate.Should().Be(isAmendment ? originalFilingDate : null);
        matchedAfter.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);

        // The unmatched row keeps its prior kind but must still advance, or it would be
        // re-selected forever.
        unmatchedAfter!.SecurityKind.Should().Be(InsiderSecurityKind.Derivative);
        unmatchedAfter.FilingForm.Should().Be(InsiderOwnershipForm.Form5);
        unmatchedAfter.IsAmendment.Should().Be(isAmendment);
        unmatchedAfter.OriginalFilingDate.Should().Be(isAmendment ? originalFilingDate : null);
        unmatchedAfter.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);

        var filingAfter = await verify
            .Set<InsiderFiling>()
            .SingleAsync(f => f.AccessionNumber == accession);
        filingAfter.FilingForm.Should().Be(InsiderOwnershipForm.Form5);
    }

    [Fact]
    public async Task Run_RowlessCachedLegacyFiling_StampsFamilyFromDocumentType()
    {
        var accession = "0000320193-24-000051";
        var rawBytes = Encoding.UTF8.GetBytes(
            "<ownershipDocument><documentType>5/A</documentType></ownershipDocument>"
        );
        DbContext.Add(
            new InsiderFiling
            {
                AccessionNumber = accession,
                FilingForm = InsiderOwnershipForm.Unknown,
                CaptureStatus = InsiderFilingCaptureStatus.Captured,
                UncompressedSize = rawBytes.Length,
                Content = new File
                {
                    Name = accession,
                    Extension = "gz",
                    ContentType = "application/gzip",
                    FileContent = new FileContent { Bytes = GzipCompressor.Compress(rawBytes) },
                },
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var runCtx = Fixture.CreateDbContext();
        var manager = new InsiderFilingReprocessManager(
            new InsiderTransactionRepository(runCtx),
            new InsiderFilingRepository(runCtx),
            new DailyStockPriceRepository(runCtx),
            new StockSplitRepository(runCtx),
            new InsiderTransactionPriceValidator(),
            Substitute.For<ISecEdgarClient>(),
            InsiderReprocessTestSupport.NewFileManager(),
            runCtx,
            NullLogger<InsiderFilingReprocessManager>()
        );

        var result = await manager.Run();

        result.Total.Should().Be(1);
        result.Processed.Should().Be(1);
        result.Failed.Should().Be(0);
        await using var verify = Fixture.CreateDbContext();
        var filing = await verify
            .Set<InsiderFiling>()
            .SingleAsync(f => f.AccessionNumber == accession);
        filing.FilingForm.Should().Be(InsiderOwnershipForm.Form5);
        (await verify.Set<InsiderTransaction>().AnyAsync(t => t.AccessionNumber == accession))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task Run_LegacyEmptyParseMarker_ConvertsToNonSectionMarkerAndClearsClaim()
    {
        var date = new DateOnly(2024, 6, 14);
        var accession = "0000320193-24-000052";
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "0001",
            Name = "Jane Insider",
        };
        var marker = new InsiderTransaction
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            InsiderOwnerId = owner.Id,
            AccessionNumber = accession,
            TransactionOrder = 0,
            FilingDate = date,
            TransactionDate = date,
            TransactionCode = TransactionCode.Other,
            SecurityTitle = "No Securities Owned",
            IsAmendment = true,
            OriginalFilingDate = date,
            SupersededAccessionNumber = "0000320193-24-000051",
            FilingForm = InsiderOwnershipForm.Form4,
            ParserVersion = 8,
        };
        var rawBytes = Encoding.UTF8.GetBytes(
            "<ownershipDocument><documentType>4/A</documentType>"
                + "<dateOfOriginalSubmission>2024-06-14</dateOfOriginalSubmission>"
                + "<nonDerivativeTable/><derivativeTable/></ownershipDocument>"
        );

        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.Add(marker);
        DbContext.Add(
            new InsiderFiling
            {
                AccessionNumber = accession,
                FilingForm = InsiderOwnershipForm.Form4,
                CaptureStatus = InsiderFilingCaptureStatus.Captured,
                UncompressedSize = rawBytes.Length,
                Content = new File
                {
                    Name = accession,
                    Extension = "gz",
                    ContentType = "application/gzip",
                    FileContent = new FileContent { Bytes = GzipCompressor.Compress(rawBytes) },
                },
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var runCtx = Fixture.CreateDbContext();
        var manager = new InsiderFilingReprocessManager(
            new InsiderTransactionRepository(runCtx),
            new InsiderFilingRepository(runCtx),
            new DailyStockPriceRepository(runCtx),
            new StockSplitRepository(runCtx),
            new InsiderTransactionPriceValidator(),
            Substitute.For<ISecEdgarClient>(),
            InsiderReprocessTestSupport.NewFileManager(),
            runCtx,
            NullLogger<InsiderFilingReprocessManager>()
        );

        var result = await manager.Run();

        result.Failed.Should().Be(0);
        result.Processed.Should().Be(1);
        await using var verify = Fixture.CreateDbContext();
        var converted = await verify.Set<InsiderTransaction>().SingleAsync(t => t.Id == marker.Id);
        converted.TransactionCode.Should().Be(TransactionCode.IngestMarker);
        converted.SecurityTitle.Should().BeNull();
        converted.SupersededAccessionNumber.Should().BeNull();
        converted.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }

    [Fact]
    public async Task Run_LegacyNoSecuritiesAmendment_RestoresAmendmentIdentity()
    {
        var originalDate = new DateOnly(2024, 6, 14);
        var accession = "0000320193-24-000053";
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "0001",
            Name = "Jane Insider",
        };
        var sentinel = new InsiderTransaction
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            InsiderOwnerId = owner.Id,
            AccessionNumber = accession,
            TransactionOrder = 0,
            FilingDate = originalDate.AddDays(1),
            TransactionDate = originalDate,
            TransactionCode = TransactionCode.Other,
            SecurityTitle = "No Securities Owned",
            IsAmendment = false,
            FilingForm = InsiderOwnershipForm.Form3,
            ParserVersion = 8,
        };
        var rawBytes = Encoding.UTF8.GetBytes(
            "<ownershipDocument><documentType>3/A</documentType>"
                + "<periodOfReport>2024-06-14</periodOfReport>"
                + "<dateOfOriginalSubmission>2024-06-14</dateOfOriginalSubmission>"
                + "<noSecuritiesOwned>1</noSecuritiesOwned>"
                + "<nonDerivativeTable/><derivativeTable/></ownershipDocument>"
        );

        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.Add(sentinel);
        DbContext.Add(
            new InsiderFiling
            {
                AccessionNumber = accession,
                FilingForm = InsiderOwnershipForm.Form3,
                CaptureStatus = InsiderFilingCaptureStatus.Captured,
                UncompressedSize = rawBytes.Length,
                Content = new File
                {
                    Name = accession,
                    Extension = "gz",
                    ContentType = "application/gzip",
                    FileContent = new FileContent { Bytes = GzipCompressor.Compress(rawBytes) },
                },
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var runCtx = Fixture.CreateDbContext();
        var manager = new InsiderFilingReprocessManager(
            new InsiderTransactionRepository(runCtx),
            new InsiderFilingRepository(runCtx),
            new DailyStockPriceRepository(runCtx),
            new StockSplitRepository(runCtx),
            new InsiderTransactionPriceValidator(),
            Substitute.For<ISecEdgarClient>(),
            InsiderReprocessTestSupport.NewFileManager(),
            runCtx,
            NullLogger<InsiderFilingReprocessManager>()
        );

        var result = await manager.Run();

        result.Failed.Should().Be(0);
        result.Processed.Should().Be(1);
        await using var verify = Fixture.CreateDbContext();
        var restored = await verify.Set<InsiderTransaction>().SingleAsync(t => t.Id == sentinel.Id);
        restored.TransactionCode.Should().Be(TransactionCode.Holding);
        restored.IsAmendment.Should().BeTrue();
        restored.OriginalFilingDate.Should().Be(originalDate);
        restored.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }
}
