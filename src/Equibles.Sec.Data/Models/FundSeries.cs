using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// One materialised row per registered-fund series, derived from the latest NPORT-P report of
/// each series. Funds have no first-class entity of their own — identity is implicit across
/// <see cref="NportFiling"/> rows — so deriving the fund directory live would mean running the
/// correlated "latest report per series" scan (<c>NportFilingRepository.GetLatestPerSeries</c>)
/// on every browse request. This table holds one indexed row per series so the <c>/funds</c>
/// index and per-fund profile become plain lookups, the same way
/// <c>HolderQuarterlySnapshot</c> backs the institutions browse.
///
/// Rebuilt wholesale by <c>FundSeriesRefreshService</c>: every series' latest report is upserted
/// keyed by <see cref="IdentityKey"/> and rows left untouched by a run (stale series) are pruned
/// by the <see cref="ComputedAt"/> watermark.
///
/// Series identity never compares name text. Exactly one of the two populations applies per row:
/// an issuer-feed filing is scoped by <see cref="CommonStockId"/> plus its non-empty
/// <see cref="SeriesId"/> (or by stock alone for an id-less standalone fund); a fund-family trust
/// series discovered by the daily-index sweep is scoped by <see cref="RegistrantCik"/> plus
/// <see cref="SeriesId"/>. If a series has filings in both populations, the newest filing decides
/// which identity is materialised. For the sweep-discovered trusts only the holdings carrying a
/// tracked stock's CUSIP are stored, so <see cref="PositionCount"/> counts the fund's positions in
/// tracked stocks, not its whole portfolio; <see cref="NetAssets"/> and
/// <see cref="TotalAssets"/> are the fund's real totals from the report header regardless.
/// </summary>
[Index(nameof(IdentityKey), IsUnique = true)]
[Index(nameof(Slug), IsUnique = true)]
[Index(nameof(LatestNportFilingId), IsUnique = true)]
[Index(nameof(CommonStockId))]
[Index(nameof(RegistrantCik), nameof(SeriesId))]
public class FundSeries
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Stable canonical identity and upsert conflict target. Derived from whichever identity
    /// population applies, never from name text: <c>cs:{CommonStockId}</c> for an id-less tracked
    /// fund, <c>cs:{CommonStockId}:{SeriesId}</c> for a tracked multi-series registrant, and
    /// <c>rc:{RegistrantCik}:{SeriesId}</c> for a sweep-discovered trust series (an id-less
    /// registrant collapses to <c>rc:{RegistrantCik}:</c>).
    /// </summary>
    [Required]
    [MaxLength(80)]
    public string IdentityKey { get; set; }

    /// <summary>
    /// URL slug for the per-fund profile route, <c>{name-slug}-{discriminator}</c>. The
    /// discriminator (series id / ticker / registrant CIK) is unique per series, so the slug is
    /// unique even when two funds share a name. Regenerated each refresh from the latest name.
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Slug { get; set; }

    /// <summary>The stock whose issuer feed supplied the filing; series id completes identity for multi-series registrants.</summary>
    public Guid? CommonStockId { get; set; }

    /// <summary>The sweep-discovered trust's registrant CIK; null for tracked funds.</summary>
    [MaxLength(16)]
    public string RegistrantCik { get; set; }

    /// <summary>The SEC series id (e.g. "S000002277"); empty string for a registrant's single id-less fund.</summary>
    [Required]
    [MaxLength(32)]
    public string SeriesId { get; set; }

    [MaxLength(512)]
    public string SeriesName { get; set; }

    [MaxLength(512)]
    public string RegistrantName { get; set; }

    /// <summary>The unambiguous SEC fund-class ticker for a series, or the tracked stock ticker for an id-less fund.</summary>
    [MaxLength(16)]
    public string Ticker { get; set; }

    /// <summary>
    /// Every exact SEC fund-class ticker assigned to this series, normalized to dash-listed form.
    /// A multi-class series has no single <see cref="Ticker"/> but retains all class tickers here.
    /// </summary>
    public List<string> ClassTickers
    {
        get => field ?? [];
        set;
    } = [];

    public DateOnly LatestReportPeriodDate { get; set; }

    public DateOnly LatestFilingDate { get; set; }

    /// <summary>
    /// The exact NPORT-P filing selected by the directory materializer. Readers join through this
    /// reference instead of repeating the latest-report identity and amendment rules.
    /// </summary>
    public Guid LatestNportFilingId { get; set; }

    /// <summary>Net assets in USD, from the latest report header.</summary>
    public decimal NetAssets { get; set; }

    /// <summary>Total assets in USD, from the latest report header.</summary>
    public decimal TotalAssets { get; set; }

    /// <summary>Stored holding rows on the latest report (tracked-stock positions only for trusts).</summary>
    public int PositionCount { get; set; }

    /// <summary>
    /// Investment rows reported on the latest NPORT-P before tracked-stock filtering. Null while
    /// that filing is waiting for parser-version replay or could not be re-fetched from EDGAR.
    /// </summary>
    public int? ReportedHoldingCount { get; set; }

    /// <summary>
    /// The N-CEN registration type of the fund (e.g. "N-1A" open-end/ETF, "N-2" closed-end), when
    /// an N-CEN filing is on record. Null for trusts and newly-launched funds with no N-CEN yet.
    /// </summary>
    [MaxLength(16)]
    public string FundType { get; set; }

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
