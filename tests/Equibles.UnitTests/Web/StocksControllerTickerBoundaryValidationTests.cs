using Equibles.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Equibles.UnitTests.Web;

public class StocksControllerTickerBoundaryValidationTests
{
    private static StocksController Controller()
    {
        var constructor = typeof(StocksController).GetConstructors().Single();
        return (StocksController)
            constructor.Invoke(constructor.GetParameters().Select(_ => (object)null).ToArray());
    }

    [Theory]
    [InlineData("ſPY")]
    [InlineData("AAPL/../../x")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567")]
    public async Task DocumentAndHolderRoutesRejectInvalidTickerBeforeDataAccess(string ticker)
    {
        var controller = Controller();

        (await controller.ShowDocument(ticker, Guid.NewGuid())).Should().BeOfType<NotFoundResult>();
        (await controller.ShowHolder(ticker, "1234")).Should().BeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData("ſ234")]
    [InlineData("12345678901234567")]
    [InlineData("0000")]
    public async Task HolderRouteRejectsInvalidCikBeforeDataAccess(string cik)
    {
        var controller = Controller();

        (await controller.ShowHolder("AAPL", cik)).Should().BeOfType<NotFoundResult>();
    }
}
