namespace Equibles.Integrations.DelayedTrades;

// One fetch of a venue's post-trade file: the evidence a capture ledger records, plus the raw bytes to parse.
public sealed record DelayedTradeFile(
    string SourceKey,
    string LocationCode,
    DelayedTradeWindow Window,
    DelayedTradeFetchOutcome Outcome,
    string SourceUrl,
    string TermsUrl,
    string Sha256,
    long Bytes,
    DateTime FetchedAtUtc,
    byte[] Content
);
