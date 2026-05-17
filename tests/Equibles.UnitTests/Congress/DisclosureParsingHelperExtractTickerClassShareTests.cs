using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperExtractTickerClassShareTests
{
    // Contract: ExtractTickerFromAssetName pulls the stock ticker out of a
    // disclosed asset name so the trade can be linked to a company. Class-share
    // tickers with a dot ("BRK.B", "BF.B") are common in real congressional
    // disclosures; they must be extracted, not dropped. (Ambiguity: the regex
    // is deliberately [A-Za-z]{1,5}; but a caller relies on dotted class
    // tickers mapping, since otherwise those trades silently lose their stock.)
    [Fact]
    public void ExtractTickerFromAssetName_DottedClassShareTicker_ExtractsFullTicker()
    {
        var result = DisclosureParsingHelper.ExtractTickerFromAssetName(
            "Berkshire Hathaway Inc. Class B (BRK.B)"
        );

        result.Should().Be("BRK.B");
    }
}
