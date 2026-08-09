using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// ParseHoldingRow's plausibility contract: a corrupt close must not price anything (the row
/// stays pending), and a derivation grossly above the filer's own figure publishes the filed
/// value instead. One corrupt series minted $102.8T of phantom value before these guards existed.
/// </summary>
public class HoldingsImportServiceParseHoldingRowSanityGuardTests
{
    private static (InstitutionalHolding Holding, bool ValuePending) Parse(
        Guid commonStockId,
        decimal? closePrice,
        string filedValueField
    )
    {
        var method = typeof(HoldingsImportService).GetMethod(
            "ParseHoldingRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var holderId = Guid.NewGuid();
        // Post-2023 filing date so VALUE is already in whole dollars.
        var filingDate = new DateOnly(2024, 11, 15);
        var reportDate = new DateOnly(2024, 9, 30);

        var row = new Dictionary<string, string>
        {
            ["SSHPRNAMTTYPE"] = "SH",
            ["PUTCALL"] = "",
            ["SSHPRNAMT"] = "273201",
            ["VALUE"] = filedValueField,
            ["VOTING_AUTH_SOLE"] = "273201",
            ["VOTING_AUTH_SHARED"] = "0",
            ["VOTING_AUTH_NONE"] = "0",
            ["OTHERMANAGER"] = "",
            ["INVESTMENTDISCRETION"] = "SOLE",
            ["TITLEOFCLASS"] = "COM",
        };

        var prices = new Dictionary<(Guid, string, DateOnly), decimal>();
        if (closePrice.HasValue)
        {
            prices[(commonStockId, null, reportDate)] = closePrice.Value;
        }

        var context = new ImportContext
        {
            CoverPages = new Dictionary<string, CoverPageRow>(),
            StockPrices = prices,
            OtherManagers = new Dictionary<string, Dictionary<int, OtherManagerIdentity>>(),
        };

        var result = method.Invoke(
            null,
            [
                row,
                "0001086364-24-008417",
                "05501M106",
                new CusipTarget(commonStockId, null),
                holderId,
                filingDate,
                reportDate,
                context,
            ]
        );

        var holding = (InstitutionalHolding)result.GetType().GetField("Item1").GetValue(result);
        var valuePending = (bool)result.GetType().GetField("Item3").GetValue(result);
        return (holding, valuePending);
    }

    [Fact]
    public void ParseHoldingRow_ImplausibleClose_LeavesRowPendingInsteadOfDeriving()
    {
        // The corrupt-series close: $285M/share. Deriving from it once stated a $13.7T position
        // against a $1.09M filing.
        var (holding, valuePending) = Parse(
            Guid.NewGuid(),
            closePrice: 285_249_984m,
            filedValueField: "1092804"
        );

        valuePending.Should().BeTrue();
        holding.Value.Should().Be(0L);
        holding.ValuePending.Should().BeTrue();
    }

    [Fact]
    public void ParseHoldingRow_DerivedGrosslyExceedsFiled_PublishesFiledValue()
    {
        // A plausible-looking close that is wrong for this stock by orders of magnitude:
        // 273,201 × $50,000 = $13.66B against a filed $1.09M.
        var (holding, valuePending) = Parse(
            Guid.NewGuid(),
            closePrice: 50_000m,
            filedValueField: "1092804"
        );

        valuePending.Should().BeFalse();
        holding.Value.Should().Be(1_092_804L);
        holding.ValueSource.Should().Be(ValueSource.Filed);
    }

    [Fact]
    public void ParseHoldingRow_FiledValueOnThousandsBasis_PublishesDerivedValue()
    {
        // A filer still reporting VALUE in thousands after the 2023 whole-dollar switch:
        // 273,201 × $208.40 derives $56.9M against a filed 56,935 — the ~1,000× unit
        // signature. The heal runs through this exact path on re-import (parser version 8).
        var (holding, valuePending) = Parse(
            Guid.NewGuid(),
            closePrice: 208.40m,
            filedValueField: "56935"
        );

        valuePending.Should().BeFalse();
        holding.Value.Should().Be((long)(273_201m * 208.40m));
        holding.ValueSource.Should().Be(ValueSource.Derived);
    }

    [Fact]
    public void ParseHoldingRow_OrdinaryDerivation_StaysDerived()
    {
        // 273,201 × $4 ≈ the filed $1.09M — inside the 5× drift ceiling, published as derived.
        var (holding, valuePending) = Parse(
            Guid.NewGuid(),
            closePrice: 4m,
            filedValueField: "1092804"
        );

        valuePending.Should().BeFalse();
        holding.Value.Should().Be(273_201L * 4L);
        holding.ValueSource.Should().Be(ValueSource.Derived);
    }
}
