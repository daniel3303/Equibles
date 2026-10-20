using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.BusinessLogic.Signals.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderInstitutionPortfolioPositionFactsTests
{
    [Fact]
    public void RenderInstitutionPortfolio_RestatesTheEstimatedCostLikeTheShares()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple"
        );
        var holding = new InstitutionalHolding
        {
            Issuer = Equibles.TestSupport.NativeListingSeed.ForStock(null, stock).Security.Issuer,
            EquityIssuerId = stock.Id,
            Shares = 100,
            Value = 6_000,
            ShareType = ShareType.Shares,
        };
        // A 2-for-1 split after the report date doubles the shares and halves the cost.
        var splits = new Dictionary<Guid, List<StockSplit>>
        {
            [stock.Id] =
            [
                new StockSplit
                {
                    EquityIssuerId = stock.Id,
                    PriceSeriesTicker = stock.Presentation.Listing.Ticker,
                    EffectiveDate = new DateOnly(2025, 1, 15),
                    Numerator = 2,
                    Denominator = 1,
                },
            ],
        };
        var facts = new Dictionary<HolderSecurityKey, HolderPositionFacts>
        {
            [new HolderSecurityKey(stock.Id, null)] = new()
            {
                HeldQuarters = 7,
                HeldSinceCoverageStart = true,
                CostStatus = HolderCostStatus.Estimated,
                EstimatedCostPerShare = 50m,
            },
        };

        var output = Render(holding, splits, facts);

        output.Should().Contain("| Held (qtrs) | Est. cost/share |");
        output.Should().Contain("| 200 |");
        output.Should().Contain("| 7+ | $25.00 |");
    }

    [Theory]
    [InlineData(HolderCostStatus.UnknownEntry, "before history")]
    [InlineData(HolderCostStatus.Unpriced, "unpriced")]
    public void RenderInstitutionPortfolio_NamesWhyACostIsMissing(
        HolderCostStatus status,
        string expected
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple"
        );
        var holding = new InstitutionalHolding
        {
            Issuer = Equibles.TestSupport.NativeListingSeed.ForStock(null, stock).Security.Issuer,
            EquityIssuerId = stock.Id,
            Shares = 100,
            Value = 6_000,
            ShareType = ShareType.Shares,
        };
        var facts = new Dictionary<HolderSecurityKey, HolderPositionFacts>
        {
            [new HolderSecurityKey(stock.Id, null)] = new()
            {
                HeldQuarters = 3,
                CostStatus = status,
            },
        };

        Render(holding, new Dictionary<Guid, List<StockSplit>>(), facts)
            .Should()
            .Contain($"| 3 | {expected} |");
    }

    private static string Render(
        InstitutionalHolding holding,
        Dictionary<Guid, List<StockSplit>> splits,
        Dictionary<HolderSecurityKey, HolderPositionFacts> facts
    )
    {
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderInstitutionPortfolio",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        return (string)
            method.Invoke(
                null,
                [
                    new InstitutionalHolder { Name = "Holder", Cik = "123" },
                    new DateOnly(2024, 12, 31),
                    new List<InstitutionalHolding> { holding },
                    splits,
                    facts,
                    0,
                    1,
                    1,
                    holding.Value,
                    0,
                    null,
                    null,
                ]
            );
    }
}
