using System.Reflection;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class CurrencyConsolidationStepGetCurrencyNameUnknownTests
{
    // GetCurrencyName feeds the rendered "All values are in {currencyName}."
    // note inserted after every consolidated table. The body's TryGetValue
    // fallback returns the raw three-letter code (e.g. "CHF" when the code
    // isn't in CurrencyMap) — NEVER null — so the rendered sentence still
    // parses cleanly. A refactor that returned `null` for unknown codes
    // would render "All values are in ." (or NRE downstream) and silently
    // break the human-readable currency context on any SEC filing using a
    // non-allowlisted ISO code (CHF, CAD, AUD, BRL, …).
    [Fact]
    public void GetCurrencyName_UnknownCurrencyCode_ReturnsRawCodeAsFallback()
    {
        var step = new CurrencyConsolidationStep();
        var method = typeof(CurrencyConsolidationStep).GetMethod(
            "GetCurrencyName",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var result = (string)method.Invoke(step, ["CHF"]);

        result.Should().Be("CHF");
    }
}
