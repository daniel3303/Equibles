using System.Reflection;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FtdImportServiceParseLineSuccessTests
{
    // Contract (per ParseLine inline comment: "Fields: SETTLEMENT DATE|CUSIP|
    // SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE"):
    //
    //   parts[0] = SettlementDate (yyyyMMdd, invariant culture)
    //   parts[1] = Cusip          (free-text, trimmed)
    //   parts[2] = Symbol         (free-text, trimmed)
    //   parts[3] = Quantity       (long)
    //   parts[4] = Description    (ignored — not stored on FtdRecord)
    //   parts[5] = Price          (decimal, invariant culture)
    //
    // The existing siblings cover the four reject arms: whitespace input,
    // short row (<6 fields), invalid date, non-numeric quantity, and
    // non-numeric price all return null. The HAPPY PATH — a fully valid
    // FTD row produces a non-null FtdRecord with every field at its
    // documented column — is unpinned. That's where the most damaging
    // regressions live: a refactor that mis-orders the property
    // assignments (e.g. `Cusip = parts[2].Trim(), Symbol = parts[1].Trim()`
    // from a copy-paste edit) would compile, pass EVERY reject pin
    // (which only assert the return is null), and silently swap CUSIP
    // and ticker symbol on every successfully-parsed FTD row in
    // production.
    //
    // Downstream consequences of a CUSIP/Symbol swap:
    //   • The FTD-to-CommonStock join keys on CUSIP9; mis-located CUSIPs
    //     route every fail-to-deliver record to the wrong stock (or to
    //     none, if "AAPL" doesn't match any CUSIP9). The /fails-to-deliver
    //     dashboard would render empty for high-fail tickers and falsely
    //     populated for unrelated ones.
    //   • The "biggest FTDs this week" alert ranks by Symbol-bucketed
    //     quantity; with Symbols carrying CUSIP values, every bucket
    //     becomes a singleton and the alert silently emits per-CUSIP
    //     noise instead of per-issuer signal.
    //
    // Additional risk this pin catches (uniquely):
    //   • Trim() drop on Cusip OR Symbol. Real FTD files DO ship padded
    //     fields (SEC sometimes space-pads ticker columns to 8 chars).
    //     The reject siblings can't catch this — they all return null,
    //     never inspecting the trimmed values. By padding the input's
    //     CUSIP and Symbol with whitespace and asserting the EXACT
    //     trimmed value, this pin makes the Trim() contract testable.
    //
    // Pin: a documented, properly-formed FTD row with leading/trailing
    // whitespace on Cusip + Symbol parses to an FtdRecord with each
    // field at its documented column AND with whitespace stripped from
    // Cusip + Symbol.
    //
    // Reflection-invoke since ParseLine is private static. FtdRecord is
    // internal; the project's InternalsVisibleTo("Equibles.UnitTests")
    // makes it directly assertable.
    [Fact]
    public void ParseLine_ValidRowWithPaddedCusipAndSymbol_TrimsAndMapsEveryFieldToItsColumn()
    {
        var line = "20240115| 037833100 | AAPL |12345|APPLE INC COMMON STOCK|185.92";

        var method = typeof(FtdImportService).GetMethod(
            "ParseLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = (FtdRecord)method.Invoke(null, [line]);

        result.Should().NotBeNull();
        result!.SettlementDate.Should().Be(new DateOnly(2024, 1, 15));
        result.Cusip.Should().Be("037833100");
        result.Symbol.Should().Be("AAPL");
        result.Quantity.Should().Be(12_345L);
        result.Price.Should().Be(185.92m);
    }
}
