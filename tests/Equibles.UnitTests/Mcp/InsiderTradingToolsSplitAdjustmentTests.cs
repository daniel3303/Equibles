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
/// Pins the split-adjustment rule for the insider MCP tools: a per-FILING quantity that is
/// displayed next to its own price/value is an as-filed record and stays as reported, so the
/// row is internally consistent (Shares × Price = Value). Only a cross-time RUNNING BALANCE
/// (post-transaction shares owned) is restated onto today's split basis. A prior regression
/// adjusted the per-row Shares while leaving Price/Value raw, so 40,000 sh @ $800 rendered a
/// $32M value beside a 400,000-share count — the row no longer reconciled (GH-2879).
/// </summary>
public class InsiderTradingToolsSplitAdjustmentTests
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

    [Fact]
    public async Task GetInsiderTransactions_TransactionBeforeSplit_KeepsRowAsFiledAndRestatesRunningBalance()
    {
        await using var db = NewDb();

        var stock = new CommonStock
        {
            Ticker = "NVDA",
            Name = "NVIDIA Corp.",
            Cik = "0001045810",
        };
        var owner = new InsiderOwner
        {
            OwnerCik = "0009876543",
            Name = "Jensen Huang",
            IsOfficer = true,
            OfficerTitle = "CEO",
        };
        db.AddRange(stock, owner);

        // 10:1 forward split effective AFTER the transaction date, so it restates the
        // running balance by a factor of 10 but leaves the as-filed row alone.
        db.Add(
            new StockSplit
            {
                CommonStockId = stock.Id,
                EffectiveDate = new DateOnly(2024, 6, 10),
                Numerator = 10,
                Denominator = 1,
                Source = StockSplitSource.Yahoo,
            }
        );
        db.Add(
            new InsiderTransaction
            {
                CommonStockId = stock.Id,
                CommonStock = stock,
                InsiderOwnerId = owner.Id,
                InsiderOwner = owner,
                TransactionDate = new DateOnly(2024, 6, 1),
                FilingDate = new DateOnly(2024, 6, 3),
                TransactionCode = TransactionCode.Sale,
                Shares = 40_000,
                PricePerShare = 800.00m,
                AcquiredDisposed = AcquiredDisposed.Disposed,
                SharesOwnedAfter = 100_000,
                OwnershipNature = OwnershipNature.Direct,
                SecurityTitle = "Common Stock",
                AccessionNumber = "acc-nvda-1",
            }
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderTransactions("NVDA");

        // The per-row Shares / Price / Value are as-filed and reconcile within the row:
        // 40,000 × $800.00 = $32,000,000.
        output.Should().Contain("| 40,000 | $800.00 | $32,000,000 |");
        // The filed quantity is NOT restated to the post-split 400,000 count...
        output.Should().NotContain("400,000");
        // ...and the value is never the inconsistent adjusted product ($320,000,000).
        output.Should().NotContain("320,000,000");
        // The running post-transaction balance IS restated onto today's 10:1 basis.
        output.Should().Contain("| 1,000,000 |");
    }

    [Fact]
    public async Task GetProposedSales_NoticeBeforeSplit_KeepsSharesAsFiledBesideMarketValue()
    {
        await using var db = NewDb();

        var stock = new CommonStock
        {
            Ticker = "NVDA",
            Name = "NVIDIA Corp.",
            Cik = "0001045810",
        };
        db.Add(stock);

        db.Add(
            new StockSplit
            {
                CommonStockId = stock.Id,
                EffectiveDate = new DateOnly(2024, 6, 10),
                Numerator = 10,
                Denominator = 1,
                Source = StockSplitSource.Yahoo,
            }
        );
        db.Add(
            new Form144Filing
            {
                CommonStockId = stock.Id,
                CommonStock = stock,
                FilingDate = new DateOnly(2024, 6, 1),
                SellerName = "Jensen Huang",
                RelationshipToIssuer = "Officer",
                SharesToBeSold = 40_000,
                AggregateMarketValue = 32_000_000m,
                ApproxSaleDate = new DateOnly(2024, 6, 5),
                BrokerName = "Acme Securities",
                AccessionNumber = "acc-144-1",
            }
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetProposedSales("NVDA");

        // Proposed Shares pair with the notice's own Aggregate Market Value; both stay
        // as filed, so the row is not restated to the post-split 400,000 count.
        output.Should().Contain("| 40,000 | $32,000,000 |");
        output.Should().NotContain("400,000");
    }
}
