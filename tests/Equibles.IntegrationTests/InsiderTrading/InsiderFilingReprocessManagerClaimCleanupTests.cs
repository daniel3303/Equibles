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

[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerClaimCleanupTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerClaimCleanupTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_CurrentRows_ClearsOnlyProvenCrossFamilyClaims()
    {
        const string mismatchedTarget = "0000000001-24-000001";
        const string matchingTarget = "0000000001-24-000002";
        const string unknownTarget = "0000000001-24-000005";
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "TEST",
            Name = "Test Company",
            Cik = "0000000001",
        };
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "0000000002",
            Name = "Test Owner",
        };
        var mismatchedClaim = BuildClaim(stock, owner, "0000000001-24-000003", mismatchedTarget);
        var matchingClaim = BuildClaim(stock, owner, "0000000001-24-000004", matchingTarget);
        var knownClaimOfUnknownTarget = BuildClaim(
            stock,
            owner,
            "0000000001-24-000006",
            unknownTarget
        );
        var unknownClaimOfKnownTarget = BuildClaim(
            stock,
            owner,
            "0000000001-24-000007",
            mismatchedTarget,
            InsiderOwnershipForm.Unknown
        );

        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.AddRange(
            new InsiderFiling
            {
                AccessionNumber = mismatchedTarget,
                FilingForm = InsiderOwnershipForm.Form4,
            },
            new InsiderFiling
            {
                AccessionNumber = matchingTarget,
                FilingForm = InsiderOwnershipForm.Form3,
            },
            new InsiderFiling { AccessionNumber = unknownTarget },
            mismatchedClaim,
            matchingClaim,
            knownClaimOfUnknownTarget,
            unknownClaimOfKnownTarget
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var runContext = Fixture.CreateDbContext();
        var manager = new InsiderFilingReprocessManager(
            new InsiderTransactionRepository(runContext),
            new InsiderFilingRepository(runContext),
            new DailyStockPriceRepository(runContext),
            new StockSplitRepository(runContext),
            new InsiderTransactionPriceValidator(),
            Substitute.For<ISecEdgarClient>(),
            Substitute.For<IFileManager>(),
            runContext,
            NullLogger<InsiderFilingReprocessManager>()
        );

        var result = await manager.Run();

        result.Total.Should().Be(0);
        await using var verify = Fixture.CreateDbContext();
        var persistedMismatch = await verify
            .Set<InsiderTransaction>()
            .SingleAsync(t => t.Id == mismatchedClaim.Id);
        var persistedMatch = await verify
            .Set<InsiderTransaction>()
            .SingleAsync(t => t.Id == matchingClaim.Id);
        var persistedKnownClaimOfUnknownTarget = await verify
            .Set<InsiderTransaction>()
            .SingleAsync(t => t.Id == knownClaimOfUnknownTarget.Id);
        var persistedUnknownClaimOfKnownTarget = await verify
            .Set<InsiderTransaction>()
            .SingleAsync(t => t.Id == unknownClaimOfKnownTarget.Id);
        persistedMismatch.SupersededAccessionNumber.Should().BeNull();
        persistedMatch.SupersededAccessionNumber.Should().Be(matchingTarget);
        persistedKnownClaimOfUnknownTarget.SupersededAccessionNumber.Should().Be(unknownTarget);
        persistedUnknownClaimOfKnownTarget.SupersededAccessionNumber.Should().Be(mismatchedTarget);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_RowlessTargetClassifiedAsDifferentFamily_ClearsClaimInSameRun(
        bool cancelAfterClassification
    )
    {
        const string targetAccession = "0000000001-24-000011";
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "TEST",
            Name = "Test Company",
            Cik = "0000000001",
        };
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "0000000002",
            Name = "Test Owner",
        };
        var claim = BuildClaim(stock, owner, "0000000001-24-000012", targetAccession);
        var rawBytes = Encoding.UTF8.GetBytes(
            "<ownershipDocument><documentType>4</documentType></ownershipDocument>"
        );

        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.Add(claim);
        DbContext.Add(
            new InsiderFiling
            {
                AccessionNumber = targetAccession,
                CaptureStatus = InsiderFilingCaptureStatus.Captured,
                UncompressedSize = rawBytes.Length,
                Content = new File
                {
                    Name = targetAccession,
                    Extension = "gz",
                    ContentType = "application/gzip",
                    FileContent = new FileContent { Bytes = GzipCompressor.Compress(rawBytes) },
                },
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var runContext = Fixture.CreateDbContext();
        var manager = new InsiderFilingReprocessManager(
            new InsiderTransactionRepository(runContext),
            new InsiderFilingRepository(runContext),
            new DailyStockPriceRepository(runContext),
            new StockSplitRepository(runContext),
            new InsiderTransactionPriceValidator(),
            Substitute.For<ISecEdgarClient>(),
            InsiderReprocessTestSupport.NewFileManager(),
            runContext,
            NullLogger<InsiderFilingReprocessManager>()
        );

        using var cancellation = new CancellationTokenSource();
        var result = await manager.Run(
            _ =>
            {
                if (cancelAfterClassification)
                    cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token
        );

        result.Total.Should().Be(1);
        result.Processed.Should().Be(1);
        await using var verify = Fixture.CreateDbContext();
        var persistedClaim = await verify
            .Set<InsiderTransaction>()
            .SingleAsync(t => t.Id == claim.Id);
        var persistedTarget = await verify
            .Set<InsiderFiling>()
            .SingleAsync(f => f.AccessionNumber == targetAccession);
        persistedTarget.FilingForm.Should().Be(InsiderOwnershipForm.Form4);
        persistedClaim.SupersededAccessionNumber.Should().BeNull();
    }

    private static InsiderTransaction BuildClaim(
        CommonStock stock,
        InsiderOwner owner,
        string accessionNumber,
        string supersededAccessionNumber,
        InsiderOwnershipForm filingForm = InsiderOwnershipForm.Form3
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            InsiderOwnerId = owner.Id,
            AccessionNumber = accessionNumber,
            TransactionOrder = 0,
            FilingDate = new DateOnly(2024, 1, 2),
            TransactionDate = new DateOnly(2024, 1, 2),
            TransactionCode = TransactionCode.Holding,
            SecurityTitle = "Common Stock",
            FilingForm = filingForm,
            ParserVersion = InsiderTransaction.CurrentParserVersion,
            SupersededAccessionNumber = supersededAccessionNumber,
        };
}
