using Equibles.Cftc.Data;
using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Repositories;
using Equibles.Tests.Helpers;
using Equibles.Web.Controllers;
using Equibles.Web.ViewModels.Cftc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.Tests.Web;

public class CftcControllerTests {
    [Fact]
    public async Task Show_MarketCodeWithSurroundingWhitespace_TrimsBeforeLookupAndReturnsContract() {
        // The `{marketCode}` route segment can pick up trailing whitespace from a
        // double-slash or trailing-space URL. `Show` is documented to .Trim() it
        // before the DB lookup; dropping that trim would 404 on otherwise-valid
        // requests, which is the kind of regression that's silent in dev (URLs
        // rarely have padding) but visible from external links.
        using var ctx = TestDbContextFactory.Create(new CftcModuleConfiguration());
        ctx.Set<CftcContract>().Add(new CftcContract {
            MarketCode = "088691",
            MarketName = "Gold (COMEX)",
            Category = CftcContractCategory.Metals,
        });
        await ctx.SaveChangesAsync();

        var contractRepo = new CftcContractRepository(ctx);
        var reportRepo = new CftcPositionReportRepository(ctx);
        var sut = new CftcController(contractRepo, reportRepo, Substitute.For<ILogger<CftcController>>());

        var result = await sut.Show("  088691  ");

        var view = result.Should().BeOfType<ViewResult>().Subject;
        var model = view.Model.Should().BeOfType<CftcContractViewModel>().Subject;
        model.MarketCode.Should().Be("088691");
        model.MarketName.Should().Be("Gold (COMEX)");
    }
}
