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
/// Pins the v7 reprocess contract: the copy-loop re-copies the Rule 10b5-1
/// checkbox from the re-parsed row. The v4 parser captured the checkbox for
/// fresh ingests, but the reprocess copy-loop never wrote it, so every
/// checkbox-era row ingested before the capture stayed null through the v4-v6
/// sweeps (#7164, EquiblesCommercial).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerRule10b5StampTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerRule10b5StampTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_V6RowWithCheckedBoxInCachedXml_StampsIsRule10b5One()
    {
        var reportDate = new DateOnly(2024, 6, 14);
        var filingDate = new DateOnly(2024, 6, 17);
        var accession = "0000320193-24-000002";

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

        // A v6 row whose filing carried the checked 10b5-1 box, ingested before the
        // v4 capture: the checkbox is null in the store even though the cached XML
        // has it. Every field except the flag is already correct.
        var stale = new InsiderTransaction
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            InsiderOwnerId = owner.Id,
            AccessionNumber = accession,
            TransactionOrder = 0,
            FilingDate = filingDate,
            TransactionDate = reportDate,
            TransactionCode = TransactionCode.Sale,
            Shares = 1000,
            PricePerShare = 55m,
            ReportedPricePerShare = 55m,
            AcquiredDisposed = AcquiredDisposed.Disposed,
            SharesOwnedAfter = 5000,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            SecurityKind = InsiderSecurityKind.NonDerivative,
            IsRule10b5One = null,
            ParserVersion = 6,
        };

        // aff10b5One is a direct value element on the document root (EDGAR 23.1),
        // not wrapped in <value> like the transaction fields.
        var ownershipXml =
            "<ownershipDocument>"
            + "<periodOfReport>2024-06-14</periodOfReport>"
            + "<aff10b5One>1</aff10b5One>"
            + "<nonDerivativeTable><nonDerivativeTransaction>"
            + "<securityTitle><value>Common Stock</value></securityTitle>"
            + "<transactionDate><value>2024-06-14</value></transactionDate>"
            + "<transactionCoding><transactionCode>S</transactionCode></transactionCoding>"
            + "<transactionAmounts>"
            + "<transactionShares><value>1000</value></transactionShares>"
            + "<transactionPricePerShare><value>55</value></transactionPricePerShare>"
            + "<transactionAcquiredDisposedCode><value>D</value></transactionAcquiredDisposedCode>"
            + "</transactionAmounts>"
            + "<postTransactionAmounts><sharesOwnedFollowingTransaction><value>5000</value>"
            + "</sharesOwnedFollowingTransaction></postTransactionAmounts>"
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
                Date = reportDate,
                Close = 55m,
            }
        );
        DbContext.Add(stale);
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

        result.Total.Should().Be(1);
        result.Processed.Should().Be(1);
        result.Rule10b5Stamped.Should().Be(1);
        result.Failed.Should().Be(0);
        // Served entirely from the cached blob — no EDGAR round-trip.
        result.Fetched.Should().Be(0);
        await edgar.DidNotReceive().GetDocumentContent(Arg.Any<string>(), Arg.Any<string>());

        await using var verify = Fixture.CreateDbContext();
        var reprocessed = await verify.Set<InsiderTransaction>().FindAsync(stale.Id);
        reprocessed!.IsRule10b5One.Should().BeTrue();
        reprocessed.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }
}
