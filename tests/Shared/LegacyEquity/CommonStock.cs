using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Data.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

[Index(nameof(Cik), IsUnique = true)]
[Index(nameof(Cusip))]
[Index(nameof(IndustryId))]
public class CommonStock : IActivable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(16)]
    public string Ticker { get; set; }

    /// <summary>
    /// Whether this issuer currently has an active listed common-stock symbol. Inactive rows are
    /// retained because their exact price series and historical holdings remain valid inputs to
    /// point-in-time analysis; ordinary stock discovery excludes them through the repository.
    /// </summary>
    public bool Active { get; set; } = true;

    /// <summary>
    /// The authoritative final trading date published by the listing reference directory. Null
    /// for active rows and legacy rows whose status has not yet been reconciled.
    /// </summary>
    public DateOnly? DelistedOn { get; set; }

    /// <summary>
    /// Last bounded Yahoo history attempt for an inactive listing. Successful backfills also add
    /// the ticker to <see cref="PriceHistoryBackfilledTickers"/>; failed responses cool down
    /// before retrying instead of occupying every price cycle.
    /// </summary>
    public DateTime? HistoricalPriceBackfillAttemptedAt { get; set; }

    /// <summary>
    /// When an authoritative inactive-listing directory requested an SEC FTD archive scan to
    /// recover this security's historical CUSIP. The sweep snapshots this timestamp so an
    /// identity discovered during a running scan receives a complete pass on the next cycle.
    /// </summary>
    public DateTime? HistoricalCusipBackfillRequestedAt { get; set; }

    /// <summary>
    /// CUSIPs observed on the newest eligible FTD settlement date during the current historical
    /// identity sweep. Claims remain staged until the entire archive has been read so a conflict
    /// discovered in a later batch cannot be forgotten.
    /// </summary>
    public List<string> HistoricalCusipBackfillCandidates { get; set; } = [];

    public DateOnly? HistoricalCusipBackfillCandidateOn { get; set; }

    public bool HistoricalCusipBackfillAmbiguous { get; set; }

    public DateTime? HistoricalCusipBackfillSweepStartedAt { get; set; }

    [MaxLength(256)]
    public string Name { get; set; }

    [MaxLength(2000)]
    public string Description { get; set; }

    [MaxLength(16)]
    public string Cik { get; set; }

    [MaxLength(256)]
    public string Website { get; set; }

    /// <summary>
    /// When website discovery last tried to fill <see cref="Website"/> (UTC), stamped
    /// when every source definitively missed. Null until first attempted. Stocks
    /// attempted within the configured cooldown are skipped, so persistent misses back
    /// off instead of re-occupying a batch slot every cycle. Transient source errors
    /// are not stamped and retry on the next cycle.
    /// </summary>
    public DateTime? WebsiteCheckedAt { get; set; }

    /// <summary>
    /// When Yahoo enrichment was last attempted for the primary ticker (UTC). Null until first
    /// attempted. Completed and failed non-cancellation attempts advance the checkpoint so an
    /// unsupported ticker cannot pin a batch; the configured interval controls its next retry.
    /// The worker selects the oldest due stocks, so a restart resumes through the universe.
    /// </summary>
    public DateTime? YahooEnrichmentAttemptedAt { get; set; }

    public double MarketCapitalization { get; set; }
    public long SharesOutStanding { get; set; }

    /// <summary>
    /// What kind of security this row's ticker is, classified from the issuer's SEC
    /// cover-page 12(b) registration table (see <see cref="ListedSecurityType"/>).
    /// <see cref="ListedSecurityType.Unknown"/> until a filing whose 12(b) table
    /// carries the ticker has been extracted.
    /// </summary>
    public ListedSecurityType ListedSecurityType { get; set; }

    /// <summary>
    /// The <c>dei:Security12bTitle</c> the classification came from (e.g.
    /// "6.875% Senior Secured Notes due 2068"), verbatim from the filing; null
    /// while <see cref="ListedSecurityType"/> is <see cref="ListedSecurityType.Unknown"/>.
    /// </summary>
    [MaxLength(500)]
    public string ListedSecurityTitle { get; set; }

    public List<string> SecondaryTickers
    {
        get => field ?? [];
        set;
    } = [];

    /// <summary>
    /// Active listed symbols established by an authoritative reference directory outside the
    /// SEC company-ticker feed. The SEC sync folds these into <see cref="SecondaryTickers"/>
    /// without owning or deleting them, so exchange-traded fund series survive its hourly
    /// refresh while every existing ticker resolver continues to use one materialized list.
    /// </summary>
    public List<string> ReferenceTickers
    {
        get => field ?? [];
        set;
    } = [];

    /// <summary>
    /// Exact listed symbols whose full Yahoo history has been committed atomically. A reference
    /// symbol absent from this set is still a grouped-daily bootstrap and must retry the deep
    /// history fetch even after more forward bars arrive.
    /// </summary>
    public List<string> PriceHistoryBackfilledTickers
    {
        get => field ?? [];
        set;
    } = [];

    public List<string> SecondaryCiks
    {
        get => field ?? [];
        set;
    } = [];

    [MaxLength(9)]
    public string Cusip { get; set; }

    /// <summary>
    /// Calendar month (1-12) the company's fiscal year ends in, sourced from
    /// SEC EDGAR's submissions <c>fiscalYearEnd</c> field. Null until detected.
    /// Off-calendar filers (e.g. Apple ≈ September, Microsoft = June) need this
    /// so quarter math reflects their real reporting periods rather than
    /// calendar quarters.
    /// </summary>
    public int? FiscalYearEndMonth { get; set; }

    /// <summary>
    /// Day of month (1-31) the company's fiscal year ends on, sourced from the
    /// same SEC field. Informational — quarter math keys off the month, since
    /// many filers use a moving "last Saturday" day that varies year to year.
    /// </summary>
    public int? FiscalYearEndDay { get; set; }

    /// <summary>
    /// SEC EDGAR's 4-digit Standard Industrial Classification code from the
    /// submissions metadata, or null until detected. Identifies pooled
    /// investment vehicles (e.g. 6221 commodity pools, 6722/6726 investment
    /// offices, 6189 asset-backed) authoritatively, so they can be told apart
    /// from operating companies without relying on ticker or name patterns.
    /// </summary>
    [MaxLength(8)]
    public string Sic { get; set; }

    /// <summary>
    /// SEC EDGAR's <c>entityType</c> from the submissions metadata — "operating"
    /// for operating companies, "other" for non-operating registrants such as
    /// unit investment trusts that carry no SIC code. Null until detected.
    /// Complements <see cref="Sic"/> when distinguishing operating companies
    /// from investment vehicles.
    /// </summary>
    [MaxLength(32)]
    public string EntityType { get; set; }

    public Guid? IndustryId { get; set; }
    public virtual Industry Industry { get; set; }
}
