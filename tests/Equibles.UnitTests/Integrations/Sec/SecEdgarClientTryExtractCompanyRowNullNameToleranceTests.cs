using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientTryExtractCompanyRowNullNameToleranceTests
{
    [Fact]
    public void TryExtractCompanyRow_NullNameWithValidCikAndTicker_ReturnsTrueWithNullName()
    {
        // SecEdgarClient.TryExtractCompanyRow (extracted in #1512) is the
        // per-row gate for `https://www.sec.gov/files/company_tickers.json`.
        // The helper enforces cik AND ticker as REQUIRED (each is checked
        // by `string.IsNullOrEmpty` in the failure disjunction) but leaves
        // `name` UNVALIDATED — the caller at SecEdgarClient.cs:674 then maps
        // a null name to empty via `Name = name ?? string.Empty`. The
        // lenient-name behaviour is intentional: the SEC tickers feed has
        // historically shipped rows where the `title` field is blank for
        // a handful of CIKs (often non-operating SPVs that nevertheless
        // need their tickers indexed for filing-archive lookups).
        //
        // The risk this catches: a refactor that "tidies up" the failure
        // disjunction to
        //   if (string.IsNullOrEmpty(cik) ||
        //       string.IsNullOrEmpty(name) ||
        //       string.IsNullOrEmpty(ticker))
        //       return false;
        // — perhaps under the false intuition that "if name matters
        // enough to extract, surely it matters enough to require" — would
        // compile, pass any test that feeds well-populated rows, and
        // silently drop every SEC company whose `title` field is empty.
        // The downstream CompanySync then never sees those CIKs, leaving
        // them un-tracked across every subsequent filing cycle. The
        // bug is invisible from CI: the active-companies count just
        // shrinks by an unrelated amount on the next sync.
        //
        // Pin the lenient-name contract: feed a row whose name slot is
        // null but whose cik and ticker are populated. The helper must
        // return true, with the out-name = null and cik+ticker set.
        var method = typeof(SecEdgarClient).GetMethod(
            "TryExtractCompanyRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // SEC's company_tickers row layout: [cik_str, title, ticker, exchange]
        // (the canonical schema). Pass cikIndex=0, nameIndex=1, tickerIndex=2.
        var row = new List<object> { "0000320193", null, "AAPL" };
        object[] args = [row, 0, 1, 2, null, null, null];

        var success = (bool)method.Invoke(null, args);

        success.Should().BeTrue();
        args[4].Should().Be("0000320193");
        args[5].Should().BeNull();
        args[6].Should().Be("AAPL");
    }
}
