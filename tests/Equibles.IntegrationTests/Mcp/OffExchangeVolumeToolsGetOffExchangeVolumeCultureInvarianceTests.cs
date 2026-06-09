using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class OffExchangeVolumeToolsGetOffExchangeVolumeCultureInvarianceTests : ParadeDbMcpTestBase
{
    private OffExchangeVolumeTools Sut() =>
        new(
            new OffExchangeVolumeRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<OffExchangeVolumeTools>()
        );

    public OffExchangeVolumeToolsGetOffExchangeVolumeCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // GetOffExchangeVolume renders the ATS / non-ATS OTC / trade-count / total cells
    // through McpFormat.WholeNumber, whose own comment promises invariant separators
    // so "the MCP markdown does not fork the separators by host locale". The repo-wide
    // contract is that LLM-facing markdown renders byte-identically on any host locale;
    // de-DE swaps the thousands separator (5,000,000 -> 5.000.000), which would fork the
    // response — the same MCP culture-forking bug class as the sibling Short* tools.
    [Fact]
    public async Task GetOffExchangeVolume_UnderNonInvariantCulture_RendersVolumesCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "GME",
            Name = "GameStop Corp",
            Cik = "0001326380",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<OffExchangeVolume>()
            .Add(
                new OffExchangeVolume
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    WeekStartDate = new DateOnly(2026, 3, 16),
                    AtsVolume = 5_000_000,
                    AtsTradeCount = 11_111,
                    NonAtsOtcVolume = 3_000_000,
                    NonAtsOtcTradeCount = 22_222,
                }
            );
        await DbContext.SaveChangesAsync();

        // Pin de-DE only for the rendering call; CurrentCulture flows through the
        // tool's await chain via ExecutionContext. Base class restores invariant.
        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut()
                .GetOffExchangeVolume("GME", startDate: "2026-01-01", endDate: "2026-04-30");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Every numeric cell must render with en-US separators on any host locale.
        // de-DE would produce 5.000.000, 3.000.000, 8.000.000, 11.111, 22.222.
        result.Should().Contain("| 5,000,000 |");
        result.Should().Contain("| 3,000,000 |");
        result.Should().Contain("| 11,111 |");
        result.Should().Contain("| 22,222 |");
        // Total off-exchange volume = ATS + non-ATS OTC = 8,000,000.
        result.Should().Contain("| 8,000,000 |");
    }
}
