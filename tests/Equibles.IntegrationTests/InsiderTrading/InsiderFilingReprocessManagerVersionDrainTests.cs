using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;
using FileContent = Equibles.Media.Data.Models.FileContent;

namespace Equibles.IntegrationTests.InsiderTrading;

/// <summary>
/// Pins the ordered parser-version drain behind issue #4374. A batch must contain only the
/// oldest remaining version so its composite index range can stream distinct accessions and
/// stop at the batch limit instead of rescanning current rows or aggregating every stale version.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerVersionDrainTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerVersionDrainTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_MultipleStaleVersions_CompletesOldestVersionBeforeAdvancing()
    {
        var date = new DateOnly(2024, 6, 14);
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
        var oldestAccessions = new[] { "0000320193-24-000071", "0000320193-24-000072" };
        var newerAccession = "0000320193-24-000073";

        var ownershipXml =
            "<ownershipDocument>"
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

        InsiderTransaction MakeStale(string accession, int parserVersion) =>
            new()
            {
                Id = Guid.NewGuid(),
                CommonStockId = stock.Id,
                InsiderOwnerId = owner.Id,
                AccessionNumber = accession,
                TransactionOrder = 0,
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
                SecurityKind = InsiderSecurityKind.NonDerivative,
                ParserVersion = parserVersion,
            };

        InsiderFiling MakeFiling(string accession) =>
            new()
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
        foreach (var accession in oldestAccessions)
        {
            DbContext.Add(MakeStale(accession, 0));
            DbContext.Add(MakeFiling(accession));
        }
        DbContext.Add(MakeStale(newerAccession, 1));
        DbContext.Add(MakeFiling(newerAccession));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using var cts = new CancellationTokenSource();
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

        var result = await manager.Run(
            _ =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            },
            cts.Token
        );

        result.Total.Should().Be(3);
        result.Processed.Should().Be(2);

        await using var verify = Fixture.CreateDbContext();
        var versions = await verify
            .Set<InsiderTransaction>()
            .ToDictionaryAsync(t => t.AccessionNumber, t => t.ParserVersion);
        versions[oldestAccessions[0]].Should().Be(InsiderTransaction.CurrentParserVersion);
        versions[oldestAccessions[1]].Should().Be(InsiderTransaction.CurrentParserVersion);
        versions[newerAccession].Should().Be(1);
    }

    [Fact]
    public async Task Run_OldestVersionFailsOnce_ExcludesItAndAdvancesToNewerVersion()
    {
        var date = new DateOnly(2024, 6, 14);
        var oldestAccession = "0000320193-24-000081";
        var newerAccession = "0000320193-24-000082";
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "0000789019",
        };
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "0002",
            Name = "John Insider",
            City = "Redmond",
            StateOrCountry = "WA",
            IsOfficer = true,
        };

        InsiderTransaction MakeStale(string accession, int parserVersion) =>
            new()
            {
                Id = Guid.NewGuid(),
                CommonStockId = stock.Id,
                InsiderOwnerId = owner.Id,
                AccessionNumber = accession,
                TransactionOrder = 0,
                FilingDate = date,
                TransactionDate = date,
                TransactionCode = TransactionCode.Purchase,
                Shares = 100,
                PricePerShare = 400m,
                ReportedPricePerShare = 400m,
                AcquiredDisposed = AcquiredDisposed.Acquired,
                SharesOwnedAfter = 1000,
                OwnershipNature = OwnershipNature.Direct,
                SecurityTitle = "Common Stock",
                SecurityKind = InsiderSecurityKind.NonDerivative,
                ParserVersion = parserVersion,
            };

        var ownershipXml =
            "<ownershipDocument>"
            + "<nonDerivativeTable><nonDerivativeTransaction>"
            + "<securityTitle><value>Common Stock</value></securityTitle>"
            + "<transactionDate><value>2024-06-14</value></transactionDate>"
            + "<transactionCoding><transactionCode>P</transactionCode></transactionCoding>"
            + "<transactionAmounts>"
            + "<transactionShares><value>100</value></transactionShares>"
            + "<transactionPricePerShare><value>400</value></transactionPricePerShare>"
            + "</transactionAmounts>"
            + "</nonDerivativeTransaction></nonDerivativeTable>"
            + "</ownershipDocument>";
        var rawBytes = Encoding.UTF8.GetBytes(ownershipXml);

        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.Add(MakeStale(oldestAccession, 0));
        DbContext.Add(MakeStale(newerAccession, 1));
        DbContext.Add(
            new InsiderFiling
            {
                AccessionNumber = newerAccession,
                CaptureStatus = InsiderFilingCaptureStatus.Captured,
                UncompressedSize = rawBytes.Length,
                Content = new File
                {
                    Name = newerAccession,
                    Extension = "gz",
                    ContentType = "application/gzip",
                    FileContent = new FileContent { Bytes = GzipCompressor.Compress(rawBytes) },
                },
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDocumentContent(oldestAccession, Arg.Any<string>())
            .Returns<string>(_ => throw new HttpRequestException("transient EDGAR failure"));

        await using var runCtx = Fixture.CreateDbContext();
        var manager = new InsiderFilingReprocessManager(
            new InsiderTransactionRepository(runCtx),
            new InsiderFilingRepository(runCtx),
            new DailyStockPriceRepository(runCtx),
            new StockSplitRepository(runCtx),
            new InsiderTransactionPriceValidator(),
            edgar,
            InsiderReprocessTestSupport.NewFileManager(),
            runCtx,
            NullLogger<InsiderFilingReprocessManager>()
        );

        var result = await manager.Run();

        result.Total.Should().Be(2);
        result.Processed.Should().Be(2);
        result.Failed.Should().Be(1);
        await edgar.Received(1).GetDocumentContent(oldestAccession, Arg.Any<string>());

        await using var verify = Fixture.CreateDbContext();
        var versions = await verify
            .Set<InsiderTransaction>()
            .ToDictionaryAsync(t => t.AccessionNumber, t => t.ParserVersion);
        versions[oldestAccession].Should().Be(0);
        versions[newerAccession].Should().Be(InsiderTransaction.CurrentParserVersion);
    }
}
