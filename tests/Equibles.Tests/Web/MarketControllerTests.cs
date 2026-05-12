using Equibles.Cboe.Data;
using Equibles.Cboe.Repositories;
using Equibles.Tests.Helpers;
using Equibles.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.Tests.Web;

public class MarketControllerTests {
    [Fact]
    public async Task PutCallRatio_UnknownTypeString_ReturnsNotFound() {
        // The route accepts {type} as a raw string and converts to CboePutCallRatioType
        // via Enum.TryParse. A regression that drops the TryParse guard would either
        // throw or fall through to a 200 with empty data — both are worse than 404
        // for an unknown ratio type.
        using var ctx = TestDbContextFactory.Create(new CboeModuleConfiguration());
        var putCallRepo = new CboePutCallRatioRepository(ctx);
        var vixRepo = new CboeVixDailyRepository(ctx);
        var sut = new MarketController(putCallRepo, vixRepo, Substitute.For<ILogger<MarketController>>());

        var result = await sut.PutCallRatio("not-a-real-ratio-type");

        result.Should().BeOfType<NotFoundResult>();
    }
}
