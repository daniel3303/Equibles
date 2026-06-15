using System.Text;
using Equibles.CommonStocks.Data.Models;
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
/// Pins the v5 holding re-tag: historical rows stored before v5 carry a Form 3/4/5
/// holding (a position snapshot) as <see cref="TransactionCode.Other"/>, which read
/// as a phantom acquisition of the insider's whole stake in transaction lists. The
/// reprocess re-parses from the cached ownership XML — where the source element is a
/// <c>nonDerivativeHolding</c> — and re-tags the row to <see cref="TransactionCode.Holding"/>,
/// authoritatively (from the element, never a DB heuristic), then stamps the version.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerRetagHoldingTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerRetagHoldingTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_StaleHoldingTaggedOther_RetagsToHoldingFromSourceElement()
    {
        var date = new DateOnly(2024, 6, 14);
        var accession = "0000320193-24-000099";

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
            OwnerCik = "0099",
            Name = "Jane Holder",
            City = "Cupertino",
            StateOrCountry = "CA",
            IsDirector = true,
        };

        // Legacy holding row: the whole position dumped into Shares (== SharesOwnedAfter),
        // no price, mis-tagged Other, stuck at the pre-v5 version. SecurityKind already
        // matches the source table so only the transaction code flips.
        var stale = new InsiderTransaction
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            InsiderOwnerId = owner.Id,
            AccessionNumber = accession,
            TransactionOrder = 0,
            FilingDate = date,
            TransactionDate = date,
            TransactionCode = TransactionCode.Other,
            Shares = 5000,
            PricePerShare = 0m,
            ReportedPricePerShare = 0m,
            AcquiredDisposed = AcquiredDisposed.Acquired,
            SharesOwnedAfter = 5000,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            SecurityKind = InsiderSecurityKind.NonDerivative,
            ParserVersion = 4,
        };

        // Source is a holding element (no transaction), so the re-parse routes through
        // ParseHolding and tags the row Holding.
        var ownershipXml =
            "<ownershipDocument>"
            + "<nonDerivativeTable><nonDerivativeHolding>"
            + "<securityTitle><value>Common Stock</value></securityTitle>"
            + "<postTransactionAmounts>"
            + "<sharesOwnedFollowingTransaction><value>5000</value></sharesOwnedFollowingTransaction>"
            + "</postTransactionAmounts>"
            + "</nonDerivativeHolding></nonDerivativeTable>"
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
        DbContext.Add(stale);
        DbContext.Add(filing);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var edgar = Substitute.For<ISecEdgarClient>();
        var fileManager = Substitute.For<IFileManager>();

        await using var runCtx = Fixture.CreateDbContext();
        var manager = new InsiderFilingReprocessManager(
            new InsiderTransactionRepository(runCtx),
            new InsiderFilingRepository(runCtx),
            new DailyStockPriceRepository(runCtx),
            new InsiderTransactionPriceValidator(),
            edgar,
            fileManager,
            runCtx,
            NullLogger<InsiderFilingReprocessManager>()
        );

        var result = await manager.Run();

        result.Total.Should().Be(1);
        result.Processed.Should().Be(1);
        result.Reclassified.Should().Be(1);
        result.Failed.Should().Be(0);
        // Served from the cached blob — no EDGAR round-trip.
        result.Fetched.Should().Be(0);
        await edgar.DidNotReceive().GetDocumentContent(Arg.Any<string>(), Arg.Any<string>());

        await using var verify = Fixture.CreateDbContext();
        var reprocessed = await verify.Set<InsiderTransaction>().FindAsync(stale.Id);
        reprocessed!.TransactionCode.Should().Be(TransactionCode.Holding);
        reprocessed.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }
}
