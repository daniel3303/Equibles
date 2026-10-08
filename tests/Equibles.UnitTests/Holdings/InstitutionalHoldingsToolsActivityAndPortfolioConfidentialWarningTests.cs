using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// "What did this fund buy?" is answered from the activity and portfolio tools, so a filer whose
/// newest 13F omits confidential positions must carry the caveat there too, not only on the summary.
/// </summary>
public class InstitutionalHoldingsToolsActivityAndPortfolioConfidentialWarningTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RenderQuarterlyActivity_WarnsOnlyForConfidentialFilers(bool confidential)
    {
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderQuarterlyActivity",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var holder = new InstitutionalHolder
        {
            Name = "Berkshire Hathaway Inc",
            ConfidentialTreatmentRequested = confidential,
        };
        var grouped = Enum.GetValues<StockPositionChangeType>()
            .ToDictionary(type => type, _ => new List<StockPositionChange>());
        grouped[StockPositionChangeType.Increased].Add(
            new StockPositionChange
            {
                CommonStockId = Guid.NewGuid(),
                Ticker = "GOOGL",
                CurrentShares = 200,
                PreviousShares = 100,
                CurrentValue = 200_000,
                PreviousValue = 100_000,
            }
        );

        var rendered = (string)
            method.Invoke(
                null,
                [
                    holder,
                    new DateOnly(2026, 6, 30),
                    new DateOnly(2026, 3, 31),
                    grouped,
                    null,
                    20,
                    null,
                ]
            );

        if (confidential)
            rendered.Should().Contain("Confidential Treatment").And.Contain("activity shown");
        else
            rendered.Should().NotContain("Confidential Treatment");
    }

    [Fact]
    public void RenderInstitutionPortfolio_ConfidentialFiler_CarriesTheWarning()
    {
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderInstitutionPortfolio",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var holder = new InstitutionalHolder
        {
            Name = "Berkshire Hathaway Inc",
            Cik = "1067983",
            ConfidentialTreatmentRequested = true,
        };
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "CB",
            Name: "Chubb Limited"
        );
        var holdings = new List<InstitutionalHolding>
        {
            new()
            {
                Issuer = Equibles
                    .TestSupport.NativeListingSeed.ForStock(null, stock)
                    .Security.Issuer,
                Shares = 1_000,
                Value = 5_000_000L,
            },
        };

        var rendered = (string)
            method.Invoke(
                null,
                [
                    holder,
                    new DateOnly(2026, 6, 30),
                    holdings,
                    new Dictionary<Guid, List<StockSplit>>(),
                    0,
                    1,
                    1,
                    5_000_000L,
                    0,
                    null,
                    null,
                ]
            );

        rendered.Should().Contain("Confidential Treatment").And.Contain("portfolio shown");
    }
}
