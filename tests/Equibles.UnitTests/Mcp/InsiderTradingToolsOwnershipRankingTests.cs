using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.InsiderTrading.Repositories;
using Equibles.Media.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Mcp;

/// <summary>
/// Pins the GetInsiderOwnership ranking contract from the MCP audit: the top-N cut must
/// compare split-ADJUSTED holdings, not raw per-filing counts. Each insider's latest row
/// sits on its own split basis — a pre-split count is smaller until restated — so cutting
/// on the raw counts silently dropped genuinely top holders whose last Form 4 predated a
/// large split while smaller post-split holders survived. Also pins the maxResults
/// argument + truncation note that replaced the hidden hard-coded 30-row cap, and the
/// canonical-ticker echo in the header.
/// </summary>
public class InsiderTradingToolsOwnershipRankingTests
{
    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
                new InsiderTradingModuleConfiguration(),
                new ErrorsModuleConfiguration(),
                // InsiderFiling navigates to Media's File (Content), so the model pulls the File
                // entity in and needs Media's configuration (the StorageProvider conversion) or
                // model finalization fails.
                new MediaModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static InsiderTradingTools Sut(EquiblesFinancialDbContext db) =>
        new(
            new InsiderTransactionRepository(db),
            new InsiderOwnerRepository(db),
            new Form144FilingRepository(db),
            new CommonStockRepository(db),
            new StockSplitRepository(db),
            new ErrorManager(new ErrorRepository(db)),
            Substitute.For<ILogger<InsiderTradingTools>>()
        );

    private static InsiderTransaction NewTransaction(
        CommonStock stock,
        InsiderOwner owner,
        DateOnly transactionDate,
        long sharesOwnedAfter,
        string accessionNumber,
        TransactionCode code = TransactionCode.Sale
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            CommonStock = stock,
            InsiderOwnerId = owner.Id,
            InsiderOwner = owner,
            TransactionDate = transactionDate,
            FilingDate = transactionDate.AddDays(2),
            TransactionCode = code,
            Shares = 100,
            PricePerShare = 50m,
            AcquiredDisposed = AcquiredDisposed.Disposed,
            SharesOwnedAfter = sharesOwnedAfter,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            AccessionNumber = accessionNumber,
        };

    [Fact]
    public async Task GetInsiderOwnership_TopNCut_RanksOnSplitAdjustedCounts()
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "NVDA",
            Name = "NVIDIA Corp.",
            Cik = "0001045810",
        };
        var preSplitHolder = new InsiderOwner
        {
            OwnerCik = "0000000001",
            Name = "Pre Split Holder",
            IsDirector = true,
        };
        var postSplitHolder = new InsiderOwner
        {
            OwnerCik = "0000000002",
            Name = "Post Split Holder",
            IsDirector = true,
        };
        db.AddRange(stock, preSplitHolder, postSplitHolder);

        // 40:1 split AFTER the pre-split holder's last filing: raw 86,080 restates to
        // 3,443,200 — a larger holding than the post-split holder's 1,000,000.
        db.Add(
            new StockSplit
            {
                CommonStockId = stock.Id,
                EffectiveDate = new DateOnly(2024, 6, 10),
                Numerator = 40,
                Denominator = 1,
                Source = StockSplitSource.Yahoo,
            }
        );
        db.Add(
            NewTransaction(
                stock,
                preSplitHolder,
                new DateOnly(2020, 3, 31),
                sharesOwnedAfter: 86_080,
                accessionNumber: "acc-pre-1"
            )
        );
        db.Add(
            NewTransaction(
                stock,
                postSplitHolder,
                new DateOnly(2024, 7, 1),
                sharesOwnedAfter: 1_000_000,
                accessionNumber: "acc-post-1"
            )
        );
        await db.SaveChangesAsync();

        // With a 1-row cut, only the genuinely larger (adjusted) holder may survive.
        // Cutting on the RAW counts would instead keep the post-split holder
        // (1,000,000 > 86,080) and drop the true top holder from the table entirely.
        var output = await Sut(db).GetInsiderOwnership("NVDA", maxResults: 1);

        output.Should().Contain("Pre Split Holder");
        output.Should().Contain("3,443,200");
        output.Should().NotContain("Post Split Holder");
    }

    [Fact]
    public async Task GetInsiderOwnership_Truncated_AppendsTruncationNote()
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var first = new InsiderOwner
        {
            OwnerCik = "0000000011",
            Name = "First Holder",
            IsDirector = true,
        };
        var second = new InsiderOwner
        {
            OwnerCik = "0000000012",
            Name = "Second Holder",
            IsDirector = true,
        };
        db.AddRange(stock, first, second);
        db.Add(
            NewTransaction(
                stock,
                first,
                new DateOnly(2024, 6, 1),
                sharesOwnedAfter: 9_000,
                accessionNumber: "acc-t-1"
            )
        );
        db.Add(
            NewTransaction(
                stock,
                second,
                new DateOnly(2024, 6, 1),
                sharesOwnedAfter: 1_000,
                accessionNumber: "acc-t-2"
            )
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderOwnership("AAPL", maxResults: 1);

        output.Should().Contain("First Holder");
        output.Should().NotContain("Second Holder");
        output.Should().Contain("Showing first 1 of 2 results - raise maxResults to see more.");
    }

    [Fact]
    public async Task GetInsiderOwnership_LowercaseTicker_EchoesCanonicalTicker()
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var owner = new InsiderOwner
        {
            OwnerCik = "0000000021",
            Name = "Case Test",
            IsDirector = true,
        };
        db.AddRange(stock, owner);
        db.Add(
            NewTransaction(
                stock,
                owner,
                new DateOnly(2024, 6, 1),
                sharesOwnedAfter: 5_000,
                accessionNumber: "acc-c-1"
            )
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderOwnership("aapl");

        output.Should().Contain("Insider ownership summary for Apple Inc. (AAPL):");
        output.Should().NotContain("(aapl)");
    }

    [Fact]
    public async Task GetInsiderOwnership_LastTransaction_RendersDisplayName()
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var owner = new InsiderOwner
        {
            OwnerCik = "0000000031",
            Name = "Withheld Shares",
            IsDirector = true,
        };
        db.AddRange(stock, owner);
        db.Add(
            NewTransaction(
                stock,
                owner,
                new DateOnly(2024, 6, 1),
                sharesOwnedAfter: 5_000,
                accessionNumber: "acc-d-1",
                code: TransactionCode.TaxPayment
            )
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderOwnership("AAPL");

        output.Should().Contain("| Tax Payment |");
        output.Should().NotContain("TaxPayment");
    }
}
