using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Data;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Sibling to the existing ShowDocument NotFound / CrossTicker pins. ShowHolder
/// has two guards: stock not found → 404, holder not found → 404. The second
/// guard (HoldingsExportController.cs:284-285) protects the very next line
/// `_stockTabService.LoadHolderDetail(stock, holder)` and the title
/// interpolation `$"{holder.Name} - ..."` from NRE-ing on a null holder.
/// /stocks/{ticker}/holders/{cik} is publicly enumerable by CIK; a refactor
/// that drops or reorders this guard would 500 on every request with a stale
/// or harvested CIK that no longer maps to a known holder, instead of the
/// expected 404. Pin the NotFound on (valid ticker, unknown cik).
/// </summary>
public class StocksControllerShowHolderUnknownCikTests
{
    [Fact]
    public async Task ShowHolder_ValidTickerWithUnknownCik_ReturnsNotFoundWithoutDereferencingNullHolder()
    {
        using var ctx = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );

        ctx.Set<CommonStock>()
            .Add(
                new CommonStock
                {
                    Id = Guid.NewGuid(),
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
        await ctx.SaveChangesAsync();

        var sut = new StocksController(
            new CommonStockRepository(ctx),
            new InstitutionalHolderRepository(ctx),
            institutionalHoldingRepository: null!,
            documentRepository: null!,
            stockTabService: null!,
            Substitute.For<ILogger<StocksController>>()
        );

        var result = await sut.ShowHolder("AAPL", "9999999999");

        result.Should().BeOfType<NotFoundResult>();
    }
}
