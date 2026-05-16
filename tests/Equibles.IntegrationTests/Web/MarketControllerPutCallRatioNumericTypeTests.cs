using Equibles.Cboe.Data;
using Equibles.Cboe.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

public class MarketControllerPutCallRatioNumericTypeTests
{
    [Fact(Skip = "GH-716 — PutCallRatio numeric type string slips past Enum.TryParse and 500s")]
    public async Task PutCallRatio_NumericTypeStringNotADefinedEnum_ReturnsNotFound()
    {
        // Contract: an unknown ratio type must yield 404 (pinned for alphabetic input
        // elsewhere). Enum.TryParse returns true for ANY numeric string in range, so
        // "999" -> (CboePutCallRatioType)999, an undefined value, slips past the guard.
        using var ctx = TestDbContextFactory.Create(new CboeModuleConfiguration());
        var putCallRepo = new CboePutCallRatioRepository(ctx);
        var vixRepo = new CboeVixDailyRepository(ctx);
        var sut = new MarketController(
            putCallRepo,
            vixRepo,
            Substitute.For<ILogger<MarketController>>()
        );

        var result = await sut.PutCallRatio("999");

        result
            .Should()
            .BeOfType<NotFoundResult>(
                "a type that is not a defined CboePutCallRatioType must 404, not pass "
                    + "through Enum.TryParse's numeric-string acceptance as an undefined enum"
            );
    }
}
