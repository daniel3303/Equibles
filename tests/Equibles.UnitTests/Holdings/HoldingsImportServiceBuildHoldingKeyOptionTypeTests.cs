using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceBuildHoldingKeyOptionTypeTests
{
    [Fact]
    public void BuildHoldingKey_DiffersByOptionTypeWhenAllOtherFieldsMatch_KeepsCallAndPutSeparate()
    {
        // HoldingsImportService.BuildHoldingKey (#1174) is the in-memory dedup key
        // for 13F holdings: every (stockId, holderId, reportDate, shareType,
        // optionType) tuple uniquely identifies one row in the source dataset.
        // Same filer disclosing both a long Call and a long Put on the same
        // stock for the same reporting quarter is routine — option positions
        // are reported separately from the underlying common-stock position
        // and from each other by call/put leg.
        //
        // The risk this catches: a refactor that drops the `optionType` field
        // from the key — perhaps under the false intuition that "option rows
        // are rare so we can collapse the dedup key" or as part of a
        // simplification that treats Call and Put as a single "option leg"
        // dimension — would compile, pass any test that doesn't disclose
        // both legs, and silently merge the Call and Put rows into one
        // dedup slot. The first row inserted wins; the second is discarded
        // as a duplicate. Result: half the option leg's shares disappear,
        // mis-stating exposure on every backtest and screener that aggregates
        // share counts.
        //
        // Pin: keep stockId, holderId, reportDate, shareType identical
        // across two calls, vary ONLY optionType (Call vs Put). The two
        // keys must differ — that is the precise property the dedup logic
        // relies on.
        var method = typeof(HoldingsImportService).GetMethod(
            "BuildHoldingKey",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(Guid), typeof(Guid), typeof(DateOnly), typeof(ShareType), typeof(OptionType?)]
        );

        var stockId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

        var callKey = (string)
            method.Invoke(
                null,
                [stockId, holderId, reportDate, ShareType.Shares, (OptionType?)OptionType.Call]
            );
        var putKey = (string)
            method.Invoke(
                null,
                [stockId, holderId, reportDate, ShareType.Shares, (OptionType?)OptionType.Put]
            );

        callKey.Should().NotBe(putKey);
    }
}
