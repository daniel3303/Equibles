using Equibles.Integrations.DelayedTrades;

namespace Equibles.Integrations.Euronext.DelayedTrades;

// The terms every figure derived from a Euronext delayed-trades file is published under.
public static class EuronextDelayedTradeTerms
{
    public const string SourceKey = "euronext";
    public const string SourceName = "Euronext";
    public const string TermsUrl = "https://www.euronext.com/delayed-data-terms-conditions";

    public const string Notice =
        "Delayed trade data provided by Euronext under its Terms and Conditions for Delayed Trade Data.";

    public static readonly DelayedTradeAttribution Attribution = new(SourceName, TermsUrl, Notice);
}
