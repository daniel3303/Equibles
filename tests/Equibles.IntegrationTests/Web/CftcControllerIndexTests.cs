using Equibles.Cftc.Data;
using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Web.Controllers;
using Equibles.Web.ViewModels.Cftc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// CftcControllerTests pins only `Show`. `Index` (`~/Futures`) was 34/34 lines
/// zero-hit in the local cobertura baseline. This pins the whole landing
/// projection: group-by-category, the latest-report-per-contract dictionary
/// join, and the commercial / non-commercial net = long − short arithmetic. A
/// regression in the join key (`CftcContractId`) or the net subtraction order
/// would silently render wrong positioning on the futures dashboard with no
/// other test catching it.
/// </summary>
public class CftcControllerIndexTests
{
    [Fact]
    public async Task Index_ContractWithLatestReport_GroupsByCategoryAndComputesNets()
    {
        using var ctx = TestDbContextFactory.Create(new CftcModuleConfiguration());
        var contract = new CftcContract
        {
            MarketCode = "088691",
            MarketName = "Gold (COMEX)",
            Category = CftcContractCategory.Metals,
        };
        ctx.Set<CftcContract>().Add(contract);
        ctx.Set<CftcPositionReport>()
            .Add(
                new CftcPositionReport
                {
                    CftcContractId = contract.Id,
                    ReportDate = new DateOnly(2025, 1, 7),
                    OpenInterest = 500_000,
                    CommLong = 100,
                    CommShort = 40, // CommercialNet = 60
                    NonCommLong = 70,
                    NonCommShort = 25, // NonCommercialNet = 45
                }
            );
        await ctx.SaveChangesAsync();

        var sut = new CftcController(
            new CftcContractRepository(ctx),
            new CftcPositionReportRepository(ctx),
            Substitute.For<ILogger<CftcController>>()
        );

        var result = await sut.Index();

        var model = result
            .Should()
            .BeOfType<ViewResult>()
            .Subject.Model.Should()
            .BeOfType<CftcIndexViewModel>()
            .Subject;
        var group = model.Categories.Should().ContainSingle().Subject;
        group.Category.Should().Be(CftcContractCategory.Metals);
        var item = group.Contracts.Should().ContainSingle().Subject;
        item.MarketCode.Should().Be("088691");
        item.CommercialNet.Should().Be(60);
        item.NonCommercialNet.Should().Be(45);
        item.LatestDate.Should().Be(new DateOnly(2025, 1, 7));
    }
}
