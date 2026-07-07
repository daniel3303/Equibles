using Equibles.CommonStocks.Data.Models;

namespace Equibles.Sec.FinancialFacts.BusinessLogic;

/// <summary>
/// Reads a stock's shares-outstanding figures from the SEC cover-page XBRL facts. Abstracted so
/// callers in other modules (e.g. the Yahoo price importer) depend on the contract rather than the
/// concrete provider, which lets module-isolated tests substitute it without pulling in the
/// FinancialFacts/SEC entity model.
/// </summary>
public interface ISharesOutstandingProvider
{
    /// <summary>
    /// The shares on the most-recently-filed consolidated cover-page fact, or null when the issuer
    /// has none on record (e.g. a multi-class filer that reports the count only per share class).
    /// </summary>
    Task<long?> GetReportedSharesOutstanding(
        CommonStock stock,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The entity total summed across a multi-class issuer's per-share-class cover-page facts, or
    /// null when no per-class facts are on record.
    /// </summary>
    Task<long?> GetSummedPerClassSharesOutstanding(
        CommonStock stock,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The issuer's current entity-wide share count, taken from the authoritative
    /// dei:EntityCommonStockSharesOutstanding cover-page tag: the more-recently-filed of the latest
    /// consolidated cover-page fact and the latest per-class sum. A single-class issuer (only a
    /// consolidated fact) or a multi-class issuer that never reported a consolidated count (only
    /// per-class facts) returns that one figure. When an issuer reports <em>both</em> — e.g. a
    /// dual-class filer whose classless cover-page series ended years ago when it switched to
    /// per-class reporting — the stale consolidated value must not win, so the figure from the
    /// most recent filing is used. The us-gaap:CommonStockSharesOutstanding balance-sheet count
    /// (frequently a nominal placeholder for shells and multi-class filers) is used only as a
    /// fallback when the issuer never reported the cover-page tag. Null when neither is on
    /// record, or when the latest cover-page count is a filing artifact — contradicted as a
    /// collapse by both the issuer's previous cover-page count and the same filing's
    /// balance-sheet count — in which case EDGAR abstains and the caller's fallback source (the
    /// price feed's listed-security count) stands.
    /// </summary>
    Task<long?> GetCurrentSharesOutstanding(
        CommonStock stock,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// True when the fact backing <see cref="GetCurrentSharesOutstanding"/> — the latest
    /// consolidated cover-page fact or the latest per-class filing, whichever wins the pick —
    /// comes from a foreign-private-issuer annual form (20-F or 40-F). Such issuers report the
    /// cover-page count in <em>ordinary</em> shares, a different unit from the US-listed ADR a
    /// price feed quotes, so the reported count must not be reconciled against an ADR market cap
    /// / price (it would inflate it by the ADR ratio). False for domestic 10-K/10-Q filers and
    /// when no shares fact is on record.
    /// </summary>
    Task<bool> IsForeignPrivateIssuer(
        CommonStock stock,
        CancellationToken cancellationToken = default
    );
}
