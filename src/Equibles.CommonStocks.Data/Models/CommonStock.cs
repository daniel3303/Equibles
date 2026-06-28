using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

[Index(nameof(Ticker), IsUnique = true)]
[Index(nameof(Cik), IsUnique = true)]
[Index(nameof(Cusip))]
[Index(nameof(IndustryId))]
public class CommonStock
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(16)]
    public string Ticker { get; set; }

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
    /// Absolute URL of the company's investor-relations page, discovered by
    /// probing common IR paths and subdomains of <see cref="Website"/>. Null
    /// until discovered (or when no IR page could be validated). Foundation for
    /// downstream IR scraping (press releases, earnings calendars, transcripts).
    /// </summary>
    [MaxLength(256)]
    public string InvestorRelationsUrl { get; set; }

    /// <summary>
    /// Platform/CMS the company's investor-relations website runs on, detected from
    /// the <see cref="InvestorRelationsUrl"/> page HTML. <see cref="IrPlatformType.Unknown"/>
    /// until an IR page is discovered and classified. Determines which IR scraper
    /// handles the company.
    /// </summary>
    public IrPlatformType IrPlatformType { get; set; }

    /// <summary>
    /// When IR discovery last probed this stock's website (UTC), stamped on every
    /// definitive outcome — an IR page found, or every candidate validated as a miss.
    /// Null until first probed. Stocks probed within the configured cooldown are
    /// skipped, so persistent misses back off instead of being re-probed every cycle.
    /// </summary>
    public DateTime? InvestorRelationsCheckedAt { get; set; }

    /// <summary>
    /// The IR-discovery code generation under which this stock was last probed (see
    /// <c>InvestorRelationsDiscoveryVersion</c>). Defaults to 0 so the whole pre-existing corpus
    /// counts as the oldest generation; when the probe logic improves and the version is bumped, a
    /// stock stamped with an older version becomes eligible for re-probe immediately, without waiting
    /// out its cooldown — so an improvement reaches the backlog of misses on deploy, not over 30 days.
    /// </summary>
    public int InvestorRelationsDiscoveryVersion { get; set; }

    /// <summary>
    /// Earliest time (UTC) this stock may be re-probed after a definitive miss. Each miss backs off
    /// exponentially — the wait doubles from the initial backoff up to the configured cap — so a
    /// transiently-blocked site (a Cloudflare "access denied", a one-off render failure) is retried
    /// within a day rather than written off for weeks, while a persistently-missing site still settles
    /// at the cap. Null until the first miss; a row stamped before this field existed (or eligible by
    /// version / website-changed) is re-probed immediately, then gets its own backoff schedule.
    /// </summary>
    public DateTime? InvestorRelationsRetryAfter { get; set; }

    /// <summary>
    /// When an IR content scraper (news/events) last worked through this stock (UTC),
    /// stamped on every cycle the stock is scraped — whether or not new rows were
    /// found. Null until first scraped. Scrapers order their cohort least-recently
    /// -scraped first (never-scraped stocks lead), so each bounded cycle advances
    /// through the whole platform cohort and then refreshes it oldest-first, instead
    /// of re-scraping the same alphabetical head every cycle.
    /// </summary>
    public DateTime? IrContentScrapedAt { get; set; }

    public double MarketCapitalization { get; set; }
    public long SharesOutStanding { get; set; }

    public List<string> SecondaryTickers
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
