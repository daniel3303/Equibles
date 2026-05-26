using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Repositories;
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
/// Sibling to StocksControllerShowDocumentCrossTickerTests (which pins the
/// stock-vs-ticker mismatch NotFound). ShowDocument's first guard
/// `if (document == null) return NotFound();` catches an unknown document id
/// before the ticker comparison can NRE on `document.CommonStock.Ticker`.
/// A refactor dropping this guard (or reordering it after the ticker check)
/// would NRE on every stale or harvested URL that points at a deleted filing —
/// the document viewer route is publicly enumerable. Pin the NotFound.
/// </summary>
public class StocksControllerShowDocumentNotFoundTests
{
    [Fact]
    public async Task ShowDocument_UnknownDocumentId_ReturnsNotFoundWithoutThrowing()
    {
        using var ctx = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration()
        );

        var sut = new StocksController(
            new CommonStockRepository(ctx),
            institutionalHolderRepository: null!,
            institutionalHoldingRepository: null!,
            new DocumentRepository(ctx),
            stockTabService: null!,
            Substitute.For<ILogger<StocksController>>()
        );

        var result = await sut.ShowDocument("AAPL", Guid.NewGuid());

        result.Should().BeOfType<NotFoundResult>();
    }
}
