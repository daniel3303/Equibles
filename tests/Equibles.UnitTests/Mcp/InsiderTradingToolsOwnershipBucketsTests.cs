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
/// Pins the ownership-summary balance rule: a Form 3/4/5 reports one closing balance PER
/// OWNERSHIP BUCKET (security title × direct/indirect), so an insider's position is the sum of
/// the newest filing's per-bucket closing balances split into Direct and Indirect columns —
/// never just the filing's last line. The last line is only whichever bucket happened to be
/// printed last: LLY's CEO filed 571,715 direct shares followed by a 7,307-share 401(k) line,
/// and the single-last-row read published 7,307 as his entire position.
/// </summary>
public class InsiderTradingToolsOwnershipBucketsTests
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

    private static InsiderTransaction Row(
        CommonStock stock,
        InsiderOwner owner,
        string accession,
        int order,
        long ownedAfter,
        OwnershipNature nature,
        string title = "Common Stock",
        DateOnly? transactionDate = null,
        DateOnly? filingDate = null
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            CommonStock = stock,
            InsiderOwnerId = owner.Id,
            InsiderOwner = owner,
            TransactionDate = transactionDate ?? new DateOnly(2026, 2, 10),
            FilingDate = filingDate ?? new DateOnly(2026, 2, 12),
            TransactionCode = TransactionCode.Sale,
            Shares = 1_000,
            PricePerShare = 100m,
            AcquiredDisposed = AcquiredDisposed.Disposed,
            SharesOwnedAfter = ownedAfter,
            OwnershipNature = nature,
            SecurityKind = InsiderSecurityKind.NonDerivative,
            SecurityTitle = title,
            AccessionNumber = accession,
            TransactionOrder = order,
        };

    private static (CommonStock Stock, InsiderOwner Owner) Fixture(EquiblesFinancialDbContext db)
    {
        var stock = new CommonStock
        {
            Ticker = "LLY",
            Name = "Eli Lilly and Company",
            Cik = "0000059478",
        };
        var owner = new InsiderOwner
        {
            OwnerCik = "0001233333",
            Name = "David Ricks",
            IsOfficer = true,
            OfficerTitle = "CEO",
        };
        db.AddRange(stock, owner);
        return (stock, owner);
    }

    // The LLY shape: the direct balance is filed BEFORE a small indirect 401(k) line, so the
    // filing's last row is the 7,307-share bucket — the old read published that as the CEO's
    // whole position.
    [Fact]
    public async Task GetInsiderOwnership_DirectAndIndirectBuckets_SumsPerBucketClosingBalances()
    {
        await using var db = NewDb();
        var (stock, owner) = Fixture(db);
        db.AddRange(
            Row(stock, owner, "acc-1", order: 0, ownedAfter: 571_715, OwnershipNature.Direct),
            Row(stock, owner, "acc-1", order: 1, ownedAfter: 7_307, OwnershipNature.Indirect)
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderOwnership("LLY");

        output.Should().Contain("| 571,715 | 7,307 | 579,022 |");
    }

    // A multi-row filing lists intermediate balances inside one bucket; only the bucket's last
    // row is its closing balance, so intermediate rows must not be summed in.
    [Fact]
    public async Task GetInsiderOwnership_IntermediateBalancesInOneBucket_TakesOnlyTheClosingRow()
    {
        await using var db = NewDb();
        var (stock, owner) = Fixture(db);
        db.AddRange(
            Row(stock, owner, "acc-1", order: 0, ownedAfter: 600_000, OwnershipNature.Direct),
            Row(stock, owner, "acc-1", order: 1, ownedAfter: 571_715, OwnershipNature.Direct),
            Row(stock, owner, "acc-1", order: 2, ownedAfter: 7_307, OwnershipNature.Indirect)
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderOwnership("LLY");

        output.Should().Contain("| 571,715 | 7,307 | 579,022 |");
        output.Should().NotContain("600,000");
    }

    // Two different securities held directly are two buckets — both closing balances count.
    [Fact]
    public async Task GetInsiderOwnership_TwoSecurityTitles_SumsBothBuckets()
    {
        await using var db = NewDb();
        var (stock, owner) = Fixture(db);
        db.AddRange(
            Row(stock, owner, "acc-1", order: 0, ownedAfter: 100_000, OwnershipNature.Direct),
            Row(
                stock,
                owner,
                "acc-1",
                order: 1,
                ownedAfter: 50_000,
                OwnershipNature.Direct,
                title: "Class B Common Stock"
            )
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderOwnership("LLY");

        output.Should().Contain("| 150,000 |");
    }

    // Only the NEWEST filing's buckets count — an older filing's balances are superseded even
    // when they are larger.
    [Fact]
    public async Task GetInsiderOwnership_OlderFiling_IsIgnored()
    {
        await using var db = NewDb();
        var (stock, owner) = Fixture(db);
        db.AddRange(
            Row(
                stock,
                owner,
                "acc-0",
                order: 0,
                ownedAfter: 900_000,
                OwnershipNature.Direct,
                transactionDate: new DateOnly(2025, 11, 1),
                filingDate: new DateOnly(2025, 11, 3)
            ),
            Row(stock, owner, "acc-1", order: 0, ownedAfter: 571_715, OwnershipNature.Direct)
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderOwnership("LLY");

        output.Should().Contain("| 571,715 |");
        output.Should().NotContain("900,000");
    }

    // Ranking uses the combined Direct + Indirect total, so a mostly-indirect holder outranks a
    // smaller all-direct one.
    [Fact]
    public async Task GetInsiderOwnership_RanksByCombinedTotal()
    {
        await using var db = NewDb();
        var (stock, owner) = Fixture(db);
        var trustee = new InsiderOwner
        {
            OwnerCik = "0001244444",
            Name = "Trust Holder",
            IsDirector = true,
        };
        db.Add(trustee);
        db.AddRange(
            Row(stock, owner, "acc-1", order: 0, ownedAfter: 100_000, OwnershipNature.Direct),
            Row(stock, trustee, "acc-2", order: 0, ownedAfter: 1_000, OwnershipNature.Direct),
            Row(stock, trustee, "acc-2", order: 1, ownedAfter: 500_000, OwnershipNature.Indirect)
        );
        await db.SaveChangesAsync();

        var output = await Sut(db).GetInsiderOwnership("LLY");

        output
            .IndexOf("Trust Holder", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("David Ricks", StringComparison.Ordinal));
        output.Should().Contain("| 1,000 | 500,000 | 501,000 |");
    }
}
