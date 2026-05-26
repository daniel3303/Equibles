using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryMapFiscalPeriodUnknownTests
{
    // TryMapFiscalPeriod follows the .NET Try* contract: on any wire value
    // outside SEC's documented {FY, Q1-Q4} set, return false and leave the
    // out parameter as default(SecFiscalPeriod). A future addition of an
    // SEC-side "H1"/"H2" half-year token (or a typo upstream) would otherwise
    // silently route through the default arm — but a regression that swapped
    // the default arm's `return false` for `return true` (a classic copy-paste
    // off-by-one) would compile and silently classify every unknown period as
    // FullYear (the default enum value), polluting the financial-fact stream.
    [Fact]
    public void TryMapFiscalPeriod_UnknownPeriodToken_ReturnsFalseWithDefaultOut()
    {
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryMapFiscalPeriod",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { "H1", default(SecFiscalPeriod) };

        var resolved = (bool)method.Invoke(null, args);

        resolved.Should().BeFalse();
        args[1].Should().Be(default(SecFiscalPeriod));
    }
}
