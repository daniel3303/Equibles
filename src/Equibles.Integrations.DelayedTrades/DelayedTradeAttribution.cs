namespace Equibles.Integrations.DelayedTrades;

// The wording a reader surface must show next to any figure derived from the source's file.
public sealed record DelayedTradeAttribution(string SourceName, string TermsUrl, string Notice);
