namespace Equibles.Messaging.Contracts.Sec;

// Raised after a Document is persisted (any form — 10-K, 10-Q, 8-K, the commercial
// earnings-call transcript, etc.), carrying enough metadata for a consumer to act
// without re-querying the financial database. The earnings-call linker uses it to
// resolve and attach artefacts to an EarningsCallEvent as filings and transcripts
// arrive. Published after the insert commits (OSS ships no transactional outbox), so
// consumers must be idempotent and the backfill closes any gap left by a missed event.
public record DocumentSaved(
    Guid DocumentId,
    Guid CommonStockId,
    string Ticker,
    string DocumentType,
    DateOnly ReportingDate,
    DateOnly ReportingForDate,
    string AccessionNumber,
    string Items
);
