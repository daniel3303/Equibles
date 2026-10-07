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
/// Pins the v12 backfill: the reprocess is the only path that derives the owner relationship
/// for rows ingested earlier, so every stored row of a filing gets the joint filers' boxes,
/// including a row the current parser no longer maps by order.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerOwnerRelationshipTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerOwnerRelationshipTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_V11RowsOfAJointFiling_StampEveryRowWithTheUnionOfBoxes()
    {
        var reportDate = new DateOnly(2024, 6, 14);
        var filingDate = new DateOnly(2024, 6, 17);
        var accession = "0000320193-24-000002";

        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "0001",
            Name = "Jane Insider",
            City = "Cupertino",
            StateOrCountry = "CA",
            IsDirector = true,
        };

        // A v11 row parsed before the relationship was recorded; every other field is correct.
        var stale = new InsiderTransaction
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = stock.Id,
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
            ParserVersion = 11,
        };
        // An older parse left a second row the current parser does not produce, so it cannot be
        // mapped by order; the document-level stamp must still reach it.
        var unmatched = new InsiderTransaction
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = stock.Id,
            InsiderOwnerId = owner.Id,
            AccessionNumber = accession,
            TransactionOrder = 1,
            FilingDate = filingDate,
            TransactionDate = reportDate,
            TransactionCode = TransactionCode.Sale,
            Shares = 200,
            PricePerShare = 55m,
            ReportedPricePerShare = 55m,
            AcquiredDisposed = AcquiredDisposed.Disposed,
            SharesOwnedAfter = 4800,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            SecurityKind = InsiderSecurityKind.NonDerivative,
            ParserVersion = 11,
        };

        // A fund that is only a 10% owner files jointly with the director who manages it.
        var ownershipXml =
            "<ownershipDocument>"
            + "<periodOfReport>2024-06-14</periodOfReport>"
            + "<reportingOwner><reportingOwnerId><rptOwnerCik>0001</rptOwnerCik></reportingOwnerId>"
            + "<reportingOwnerRelationship><isTenPercentOwner>1</isTenPercentOwner></reportingOwnerRelationship>"
            + "</reportingOwner>"
            + "<reportingOwner><reportingOwnerId><rptOwnerCik>0002</rptOwnerCik></reportingOwnerId>"
            + "<reportingOwnerRelationship><isDirector>1</isDirector></reportingOwnerRelationship>"
            + "</reportingOwner>"
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
            new EquityDailyStockPrice
            {
                Listing = Equibles.TestSupport.NativeListingSeed.ForStock(DbContext, stock, null),
                Date = reportDate,
                Close = 55m,
            }
        );
        DbContext.Add(stale);
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
            new EquityDailyStockPriceRepository(runCtx),
            new StockSplitRepository(runCtx),
            new InsiderTransactionPriceValidator(),
            edgar,
            fileManager,
            runCtx,
            NullLogger<InsiderFilingReprocessManager>()
        );

        var result = await manager.Run();

        result.Processed.Should().Be(1);
        result.Failed.Should().Be(0);

        await using var verify = Fixture.CreateDbContext();
        var rows = await verify
            .Set<InsiderTransaction>()
            .Where(t => t.AccessionNumber == accession)
            .ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should()
            .OnlyContain(t =>
                t.OwnerRelationship
                    == (InsiderRelationship.TenPercentOwner | InsiderRelationship.Director)
                && t.ParserVersion == InsiderTransaction.CurrentParserVersion
            );
    }
}
