using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

// AttachSecondaryCik/DetachSecondaryCik contract (GH-7041): an attached CIK is
// normalized (leading zeros trimmed), refused when it is the stock's own primary,
// already attached, or owned by ANY other stock (primary or secondary — one CIK
// belongs to one stock, ever), and a successful attach publishes
// StockSecondaryCikAttached via the root bus so the facts checkpoint resets.
public class CommonStockManagerAttachSecondaryCikTests
{
    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );
    }

    private static (CommonStockManager Manager, CommonStockRepository Repo, IBus Bus, EquiblesFinancialDbContext Db) NewSut()
    {
        var db = NewDb();
        var repo = Substitute.ForPartsOf<CommonStockRepository>(db);
        var bus = Substitute.For<IBus>();
        return (new CommonStockManager(repo, bus), repo, bus, db);
    }

    [Fact]
    public async Task Attach_NewCik_AppendsNormalizedPublishesAndSaves()
    {
        var (sut, _, bus, db) = NewSut();
        var stock = new CommonStock { Ticker = "XOM", Name = "Exxon Mobil", Cik = "2115436" };
        db.Add(stock);
        await db.SaveChangesAsync();

        var error = await sut.AttachSecondaryCik(stock, "0000034088");

        error.Should().BeNull();
        stock.SecondaryCiks.Should().Contain("34088");
        await bus.Received(1)
            .Publish(
                Arg.Is<StockSecondaryCikAttached>(e =>
                    e.CommonStockId == stock.Id && e.Ticker == "XOM" && e.Cik == "34088"
                )
            );
    }

    [Fact]
    public async Task Attach_PrimaryCikOfSameStock_RefusesWithoutPublishing()
    {
        var (sut, repo, bus, db) = NewSut();
        var stock = new CommonStock { Ticker = "XOM", Name = "Exxon Mobil", Cik = "2115436" };
        db.Add(stock);
        await db.SaveChangesAsync();

        // Leading zeros must not disguise the stock's own primary CIK.
        var error = await sut.AttachSecondaryCik(stock, "0002115436");

        error.Should().Contain("already this stock's primary CIK");
        stock.SecondaryCiks.Should().BeEmpty();
        await bus.DidNotReceive().Publish(Arg.Any<StockSecondaryCikAttached>());
        await repo.DidNotReceive().SaveChanges();
    }

    [Fact]
    public async Task Attach_AlreadyAttachedCik_Refuses()
    {
        var (sut, _, bus, db) = NewSut();
        var stock = new CommonStock
        {
            Ticker = "XOM",
            Name = "Exxon Mobil",
            Cik = "2115436",
            SecondaryCiks = ["34088"],
        };
        db.Add(stock);
        await db.SaveChangesAsync();

        var error = await sut.AttachSecondaryCik(stock, "34088");

        error.Should().Contain("already attached");
        await bus.DidNotReceive().Publish(Arg.Any<StockSecondaryCikAttached>());
    }

    [Fact]
    public async Task Attach_CikOwnedByAnotherStockAsPrimary_Refuses()
    {
        var (sut, _, bus, db) = NewSut();
        var stock = new CommonStock { Ticker = "XOM", Name = "Exxon Mobil", Cik = "2115436" };
        var other = new CommonStock { Ticker = "CVX", Name = "Chevron", Cik = "93410" };
        db.Add(stock);
        db.Add(other);
        await db.SaveChangesAsync();

        var error = await sut.AttachSecondaryCik(stock, "93410");

        error.Should().Contain("already belongs to CVX");
        stock.SecondaryCiks.Should().BeEmpty();
        await bus.DidNotReceive().Publish(Arg.Any<StockSecondaryCikAttached>());
    }

    [Fact]
    public async Task Attach_CikOwnedByAnotherStockAsSecondary_Refuses()
    {
        var (sut, _, bus, db) = NewSut();
        var stock = new CommonStock { Ticker = "XOM", Name = "Exxon Mobil", Cik = "2115436" };
        var other = new CommonStock
        {
            Ticker = "CVX",
            Name = "Chevron",
            Cik = "93410",
            SecondaryCiks = ["34088"],
        };
        db.Add(stock);
        db.Add(other);
        await db.SaveChangesAsync();

        var error = await sut.AttachSecondaryCik(stock, "34088");

        error.Should().Contain("already belongs to CVX");
        await bus.DidNotReceive().Publish(Arg.Any<StockSecondaryCikAttached>());
    }

    [Fact]
    public async Task Attach_InvalidCik_Refuses()
    {
        var (sut, _, bus, db) = NewSut();
        var stock = new CommonStock { Ticker = "XOM", Name = "Exxon Mobil", Cik = "2115436" };
        db.Add(stock);
        await db.SaveChangesAsync();

        (await sut.AttachSecondaryCik(stock, "not-a-cik")).Should().Contain("not a valid CIK");
        (await sut.AttachSecondaryCik(stock, "")).Should().Contain("not a valid CIK");
        (await sut.AttachSecondaryCik(stock, "12345678901")).Should().Contain("not a valid CIK");
        await bus.DidNotReceive().Publish(Arg.Any<StockSecondaryCikAttached>());
    }

    [Fact]
    public async Task Detach_AttachedCik_RemovesWithoutTouchingOthers()
    {
        var (sut, _, _, db) = NewSut();
        var stock = new CommonStock
        {
            Ticker = "XOM",
            Name = "Exxon Mobil",
            Cik = "2115436",
            SecondaryCiks = ["34088", "99999"],
        };
        db.Add(stock);
        await db.SaveChangesAsync();

        var error = await sut.DetachSecondaryCik(stock, "0000034088");

        error.Should().BeNull();
        stock.SecondaryCiks.Should().Equal("99999");
    }

    [Fact]
    public async Task Detach_ZeroPaddedStoredCik_StillRemoves()
    {
        // The company sync's subsidiary attach stores SEC's value verbatim, so a
        // zero-padded CIK can live in the column; the detach comparison must
        // normalize BOTH sides or that entry becomes unremovable.
        var (sut, _, _, db) = NewSut();
        var stock = new CommonStock
        {
            Ticker = "XOM",
            Name = "Exxon Mobil",
            Cik = "2115436",
            SecondaryCiks = ["0000034088"],
        };
        db.Add(stock);
        await db.SaveChangesAsync();

        var error = await sut.DetachSecondaryCik(stock, "34088");

        error.Should().BeNull();
        stock.SecondaryCiks.Should().BeEmpty();
    }

    [Fact]
    public async Task Detach_NotAttachedCik_Refuses()
    {
        var (sut, repo, _, db) = NewSut();
        var stock = new CommonStock { Ticker = "XOM", Name = "Exxon Mobil", Cik = "2115436" };
        db.Add(stock);
        await db.SaveChangesAsync();

        var error = await sut.DetachSecondaryCik(stock, "34088");

        error.Should().Contain("not attached");
        await repo.DidNotReceive().SaveChanges();
    }

    [Theory]
    [InlineData("0000034088", "34088")]
    [InlineData(" 34088 ", "34088")]
    [InlineData("34088", "34088")]
    [InlineData("1234567890", "1234567890")]
    public void NormalizeCik_ValidForms_TrimAndDropLeadingZeros(string raw, string expected)
    {
        CommonStockManager.NormalizeCik(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("0000")]
    [InlineData("34O88")]
    [InlineData("12345678901")]
    [InlineData("-34088")]
    public void NormalizeCik_InvalidForms_ReturnNull(string raw)
    {
        CommonStockManager.NormalizeCik(raw).Should().BeNull();
    }
}
