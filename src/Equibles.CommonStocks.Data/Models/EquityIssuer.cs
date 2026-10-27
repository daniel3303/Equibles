using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

// Legal issuer identity is independent of its securities and exchange listings.
[Index(nameof(LegalEntityIdentifier), IsUnique = true)]
public class EquityIssuer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(500)]
    public string Name { get; set; }

    // Official source names aid discovery only; they never resolve issuer identity.
    [Required]
    public List<string> NameAliases { get; set; } = [];

    [MaxLength(2000)]
    public string Description { get; set; }

    [MaxLength(16)]
    public string Cik { get; set; }

    [MaxLength(20)]
    public string LegalEntityIdentifier { get; set; }

    public List<string> SecondaryCiks
    {
        get => field ?? [];
        set;
    } = [];

    [MaxLength(256)]
    public string Website { get; set; }

    public DateTime? WebsiteCheckedAt { get; set; }

    /// <summary>
    /// Website discovery rules version that last judged <see cref="Website"/>; an older value
    /// queues the stored website for its sources to re-check under their current rules.
    /// </summary>
    public int WebsiteDiscoveryVersion { get; set; }

    /// <summary>
    /// When a re-check's replacement website first failed the reachability probe; the stock
    /// is re-checked once more a day later, since a probe that could not run reads as a dead site.
    /// </summary>
    public DateTime? WebsiteRecheckProbeFailedAt { get; set; }
    public int? FiscalYearEndMonth { get; set; }
    public int? FiscalYearEndDay { get; set; }

    [MaxLength(8)]
    public string Sic { get; set; }

    [MaxLength(32)]
    public string EntityType { get; set; }

    // EDGAR state-or-country codes from the submissions header (decode with EdgarStateCodes);
    // null when SEC records none. Neither proves a home country alone: a US-incorporated
    // company can keep its principal offices abroad.
    [MaxLength(8)]
    public string StateOfIncorporation { get; set; }

    [MaxLength(8)]
    public string BusinessStateOrCountry { get; set; }

    // The file number EDGAR indexes the newest periodic report under, which can differ from
    // the number the report's cover states.
    [MaxLength(32)]
    public string PeriodicReportFileNumber { get; set; }

    // Newest Form N-54A (election to be regulated as a business development company) and
    // N-54C (withdrawal of that election) seen in the filing index. Never cleared when an
    // old election leaves EDGAR's recent-filings list.
    public DateOnly? BdcElectionFiledOn { get; set; }
    public DateOnly? BdcWithdrawalFiledOn { get; set; }

    // When the whole filing history, archived pages included, was first read for those forms;
    // null while only the recent list has been read.
    public DateOnly? BdcHistoryReadOn { get; set; }

    // When EDGAR's submissions header was last checked for the fields above, including a check
    // that found the filer missing or its reply unreadable; null until first checked.
    public DateOnly? SecRegistrantProfileReadOn { get; set; }

    public Guid? IndustryId { get; set; }
    public virtual Industry Industry { get; set; }
    public virtual List<EquitySecurity> Securities { get; set; } = [];
    public virtual EquityIssuerPresentation Presentation { get; set; }

    [MaxLength(2000)]
    public string IdentitySourceUrl { get; set; }
}
