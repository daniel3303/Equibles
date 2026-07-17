using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
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
/// Pins the GetInsiderTransactions rendering and filter contract introduced by the MCP
/// audit: the Buy/Sell labels are reserved for the open-market Purchase/Sale codes and
/// every other SEC code renders its own meaning (a Conversion or Tax Payment reported
/// as "Buy"/"Sell" told the model an open-market trade happened that never did); the
/// Rule 10b5-1 plan flag is surfaced; degenerate zero-share/zero-balance rows are
/// dropped; and the date-range/transactionType arguments reject unparseable values
/// instead of silently ignoring them.
/// </summary>
public class InsiderTradingToolsTransactionRenderingAndFilterTests
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

    private static CommonStock NewStock() =>
        new()
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };

    private static InsiderOwner NewOwner(string name = "John Doe", string cik = "0001234567") =>
        new()
        {
            OwnerCik = cik,
            Name = name,
            IsDirector = true,
        };

    private static InsiderTransaction NewTransaction(
        CommonStock stock,
        InsiderOwner owner,
        TransactionCode code,
        AcquiredDisposed acquiredDisposed,
        string accessionNumber,
        DateOnly? transactionDate = null,
        long shares = 1000,
        decimal pricePerShare = 100m,
        long sharesOwnedAfter = 5000,
        bool? isRule10b5One = null
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            CommonStock = stock,
            InsiderOwnerId = owner.Id,
            InsiderOwner = owner,
            TransactionDate = transactionDate ?? new DateOnly(2024, 6, 1),
            FilingDate = (transactionDate ?? new DateOnly(2024, 6, 1)).AddDays(2),
            TransactionCode = code,
            Shares = shares,
            PricePerShare = pricePerShare,
            AcquiredDisposed = acquiredDisposed,
            SharesOwnedAfter = sharesOwnedAfter,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            AccessionNumber = accessionNumber,
            IsRule10b5One = isRule10b5One,
        };

    [Fact]
    public async Task GetInsiderTransactions_ConversionRow_RendersConversionNotBuy()
    {
        await using var db = NewDb();
        var stock = NewStock();
        var owner = NewOwner();
        db.AddRange(stock, owner);
        db.Add(
            NewTransaction(
                stock,
                owner,
                TransactionCode.Conversion,
                AcquiredDisposed.Acquired,
                "acc-1"
            )
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderTransactions("AAPL");

        output.Should().Contain("| Conversion |");
        output.Should().NotContain("| Buy |");
    }

    [Fact]
    public async Task GetInsiderTransactions_TaxPaymentRow_RendersTaxPaymentNotSell()
    {
        await using var db = NewDb();
        var stock = NewStock();
        var owner = NewOwner();
        db.AddRange(stock, owner);
        db.Add(
            NewTransaction(
                stock,
                owner,
                TransactionCode.TaxPayment,
                AcquiredDisposed.Disposed,
                "acc-1"
            )
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderTransactions("AAPL");

        output.Should().Contain("| Tax Payment |");
        output.Should().NotContain("| Sell |");
    }

    [Fact]
    public async Task GetInsiderTransactions_Rule10b5OneFlag_RendersYesNoAndDash()
    {
        await using var db = NewDb();
        var stock = NewStock();
        var owner = NewOwner();
        db.AddRange(stock, owner);
        db.Add(
            NewTransaction(
                stock,
                owner,
                TransactionCode.Sale,
                AcquiredDisposed.Disposed,
                "acc-1",
                transactionDate: new DateOnly(2024, 6, 3),
                isRule10b5One: true
            )
        );
        db.Add(
            NewTransaction(
                stock,
                owner,
                TransactionCode.Sale,
                AcquiredDisposed.Disposed,
                "acc-2",
                transactionDate: new DateOnly(2024, 6, 2),
                isRule10b5One: false
            )
        );
        db.Add(
            NewTransaction(
                stock,
                owner,
                TransactionCode.Sale,
                AcquiredDisposed.Disposed,
                "acc-3",
                transactionDate: new DateOnly(2024, 6, 1),
                isRule10b5One: null
            )
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderTransactions("AAPL");

        output.Should().Contain("| 10b5-1 |");
        output.Should().Contain("| Yes |");
        output.Should().Contain("| No |");
        output.Should().Contain("| - |");
    }

    [Fact]
    public async Task GetInsiderTransactions_ZeroShareZeroBalanceRow_IsDropped()
    {
        await using var db = NewDb();
        var stock = NewStock();
        var real = NewOwner(name: "Real Trader", cik: "0000000001");
        var ghost = NewOwner(name: "Ghost Filer", cik: "0000000002");
        db.AddRange(stock, real, ghost);
        db.Add(
            NewTransaction(
                stock,
                real,
                TransactionCode.Purchase,
                AcquiredDisposed.Acquired,
                "acc-1"
            )
        );
        // A parser artifact / no-securities Form 3: zero shares, zero price, zero balance.
        db.Add(
            NewTransaction(
                stock,
                ghost,
                TransactionCode.Other,
                AcquiredDisposed.Acquired,
                "acc-2",
                shares: 0,
                pricePerShare: 0m,
                sharesOwnedAfter: 0
            )
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderTransactions("AAPL");

        output.Should().Contain("Real Trader");
        output.Should().NotContain("Ghost Filer");
        output.Should().Contain("Showing 1 most recent transactions");
    }

    [Fact]
    public async Task GetInsiderTransactions_InvalidFromDate_ReturnsStrictError()
    {
        await using var db = NewDb();
        var stock = NewStock();
        db.Add(stock);
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderTransactions("AAPL", fromDate: "June 2024");

        output.Should().Be("Unknown fromDate 'June 2024'. Accepted: yyyy-MM-dd.");
    }

    [Fact]
    public async Task GetInsiderTransactions_InvalidTransactionType_ListsAcceptedValues()
    {
        await using var db = NewDb();
        var stock = NewStock();
        db.Add(stock);
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderTransactions("AAPL", transactionType: "Bought");

        output
            .Should()
            .Be(
                "Unknown transactionType 'Bought'. Accepted: Buy, Sell, Award, Conversion, Exercise, TaxPayment, Expiration, Gift, Inheritance, Discretionary, Other."
            );
    }

    [Fact]
    public async Task GetInsiderTransactions_DateRange_LimitsToWindow()
    {
        await using var db = NewDb();
        var stock = NewStock();
        var owner = NewOwner();
        db.AddRange(stock, owner);
        db.Add(
            NewTransaction(
                stock,
                owner,
                TransactionCode.Purchase,
                AcquiredDisposed.Acquired,
                "acc-1",
                transactionDate: new DateOnly(2024, 1, 15)
            )
        );
        db.Add(
            NewTransaction(
                stock,
                owner,
                TransactionCode.Purchase,
                AcquiredDisposed.Acquired,
                "acc-2",
                transactionDate: new DateOnly(2024, 3, 15)
            )
        );
        db.Add(
            NewTransaction(
                stock,
                owner,
                TransactionCode.Purchase,
                AcquiredDisposed.Acquired,
                "acc-3",
                transactionDate: new DateOnly(2024, 6, 15)
            )
        );
        await db.SaveChangesAsync();

        var output = await Sut(db)
            .GetInsiderTransactions("AAPL", fromDate: "2024-02-01", toDate: "2024-05-01");

        output.Should().Contain("2024-03-15");
        output.Should().NotContain("2024-01-15");
        output.Should().NotContain("2024-06-15");
    }

    [Fact]
    public async Task GetInsiderTransactions_TransactionTypeBuy_FiltersToOpenMarketPurchases()
    {
        await using var db = NewDb();
        var stock = NewStock();
        var owner = NewOwner();
        db.AddRange(stock, owner);
        db.Add(
            NewTransaction(
                stock,
                owner,
                TransactionCode.Purchase,
                AcquiredDisposed.Acquired,
                "acc-1"
            )
        );
        db.Add(
            NewTransaction(stock, owner, TransactionCode.Sale, AcquiredDisposed.Disposed, "acc-2")
        );
        db.Add(
            NewTransaction(stock, owner, TransactionCode.Award, AcquiredDisposed.Acquired, "acc-3")
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderTransactions("AAPL", transactionType: "Buy");

        output.Should().Contain("| Buy |");
        output.Should().NotContain("| Sell |");
        output.Should().NotContain("| Award |");
    }

    [Fact]
    public async Task GetInsiderTransactions_Truncated_AppendsTruncationNote()
    {
        await using var db = NewDb();
        var stock = NewStock();
        var owner = NewOwner();
        db.AddRange(stock, owner);
        for (var i = 0; i < 3; i++)
        {
            db.Add(
                NewTransaction(
                    stock,
                    owner,
                    TransactionCode.Purchase,
                    AcquiredDisposed.Acquired,
                    $"acc-{i}",
                    transactionDate: new DateOnly(2024, 1 + i, 1)
                )
            );
        }
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderTransactions("AAPL", maxResults: 2);

        output.Should().Contain("Showing first 2 of 3 results - raise maxResults to see more.");
    }

    [Fact]
    public async Task GetInsiderTransactions_NotTruncated_HasNoTruncationNote()
    {
        await using var db = NewDb();
        var stock = NewStock();
        var owner = NewOwner();
        db.AddRange(stock, owner);
        db.Add(
            NewTransaction(
                stock,
                owner,
                TransactionCode.Purchase,
                AcquiredDisposed.Acquired,
                "acc-1"
            )
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderTransactions("AAPL");

        output.Should().NotContain("raise maxResults to see more");
    }

    [Fact]
    public async Task GetInsiderTransactions_LowercaseTicker_EchoesCanonicalTicker()
    {
        await using var db = NewDb();
        var stock = NewStock();
        var owner = NewOwner();
        db.AddRange(stock, owner);
        db.Add(
            NewTransaction(
                stock,
                owner,
                TransactionCode.Purchase,
                AcquiredDisposed.Acquired,
                "acc-1"
            )
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderTransactions("aapl");

        output.Should().Contain("Apple Inc. (AAPL)");
        output.Should().NotContain("(aapl)");
    }
}
