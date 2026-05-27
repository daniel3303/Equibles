using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceBuildHoldingKeyShareTypeTests
{
    // Sibling to the existing OptionType and FilingType differentiation pins.
    // BuildHoldingKey's `(int)shareType` segment separates two genuinely
    // distinct 13F row classes that ride the same (stockId, holderId,
    // reportDate, optionType, filingType) tuple: bond Principal positions
    // (notional dollars) vs equity Shares (count). A filer holding the
    // same issuer's convertible bond AND its common stock on the same date
    // legitimately reports both rows — collapsing them into one dedup slot
    // would either keep the dollar Principal as a share count or vice versa,
    // mis-stating exposure by a factor of the issuer's per-share price.
    // A refactor that prunes `(int)shareType` from the key ("Principal is
    // rare, share dedup is fine") would compile, pass every existing test,
    // and silently lose half of every mixed-claim filer's rows.
    [Fact]
    public void BuildHoldingKey_DiffersByShareTypeWhenAllOtherFieldsMatch_KeepsSharesAndPrincipalSeparate()
    {
        var method = typeof(HoldingsImportService).GetMethod(
            "BuildHoldingKey",
            BindingFlags.NonPublic | BindingFlags.Static,
            [
                typeof(Guid),
                typeof(Guid),
                typeof(DateOnly),
                typeof(ShareType),
                typeof(OptionType?),
                typeof(FilingType),
            ]
        );

        var stockId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

        var sharesKey = (string)
            method.Invoke(
                null,
                [
                    stockId,
                    holderId,
                    reportDate,
                    ShareType.Shares,
                    (OptionType?)null,
                    FilingType.Form13F,
                ]
            );
        var principalKey = (string)
            method.Invoke(
                null,
                [
                    stockId,
                    holderId,
                    reportDate,
                    ShareType.Principal,
                    (OptionType?)null,
                    FilingType.Form13F,
                ]
            );

        sharesKey.Should().NotBe(principalKey);
    }
}
