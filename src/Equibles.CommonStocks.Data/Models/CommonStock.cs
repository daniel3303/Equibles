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
