using System.Reflection;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class CurrencyConsolidationStepRemoveCurrencySymbolsWordBoundaryTests
{
    // RemoveCurrencySymbols must strip a currency symbol ($) and a STANDALONE
    // ISO code (USD) while preserving an acronym that merely embeds the code
    // (USDA). The code removal uses a \bUSD\b word-boundary regex, not a plain
    // Replace — a plain Replace would shred "USDA" into "A". Same word-boundary
    // contract DetectCurrency relies on, but on the removal path.
    [Fact]
    public void RemoveCurrencySymbols_AcronymEmbeddingCode_PreservesAcronymAndStripsSymbol()
    {
        var method = typeof(CurrencyConsolidationStep).GetMethod(
            "RemoveCurrencySymbols",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new CurrencyConsolidationStep();

        var result = (string)method.Invoke(step, ["USDA $5"]);

        // "$" stripped; "USDA" left intact (no \bUSD\b boundary inside it).
        result.Should().Be("USDA 5");
    }
}
