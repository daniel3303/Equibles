using Equibles.CommonStocks.Data.Models;
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

namespace Equibles.IntegrationTests.InsiderTrading;

/// <summary>
/// Cancellation pin for the reprocess loop. <c>InsiderFilingReprocessResult.Processed</c> is
/// documented as "Filings processed so far this run (including failures)" — a filing skipped
/// because the token was cancelled mid-batch was never attempted, so it must not be counted
/// as processed and its rows must stay below the current parser version for the next run.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerCancellationTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerCancellationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact(
        Skip = "GH-3507 — Run credits the whole batch to Processed when cancellation skips filings mid-batch"
    )]
    public async Task Run_CancelledMidBatch_CountsOnlyAttemptedFilingsAsProcessed()
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

        // Two stale filings in the same batch, neither cached, so each needs an EDGAR fetch.
        InsiderTransaction MakeStale(string accession) =>
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
                ParserVersion = 0,
            };

        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.Add(MakeStale("0000320193-24-000077"));
        DbContext.Add(MakeStale("0000320193-24-000078"));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

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

        // The first filing's EDGAR fetch cancels the token: that filing completes, and the
        // batch's remaining filing must be skipped and left pending for the next run.
        using var cts = new CancellationTokenSource();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDocumentContent(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci =>
            {
                cts.Cancel();
                return ownershipXml;
            });
        var fileManager = Substitute.For<IFileManager>();
        fileManager
            .SaveInternalFile(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            )
            .Returns(ci => new File
            {
                Name = ci.ArgAt<string>(1),
                Extension = "gz",
                ContentType = "application/gzip",
            });

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

        var result = await manager.Run(cancellationToken: cts.Token);

        result.Fetched.Should().Be(1);
        result.Failed.Should().Be(0);
        result
            .Processed.Should()
            .Be(1, "only the filing attempted before cancellation was processed");

        await using var verify = Fixture.CreateDbContext();
        var advanced = await verify
            .Set<InsiderTransaction>()
            .CountAsync(t => t.ParserVersion == InsiderTransaction.CurrentParserVersion);
        advanced
            .Should()
            .Be(1, "the skipped filing must remain below the current version for the next run");
    }
}
