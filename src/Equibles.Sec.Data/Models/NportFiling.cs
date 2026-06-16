using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// A SEC Form NPORT-P monthly portfolio report filed by a registered investment company (mutual
/// fund, ETF or closed-end fund) for one of its series.
///
/// A filing reaches the database two ways. Funds that are themselves tracked issuers (listed
/// closed-end funds, standalone ETF trusts) are crawled through their EDGAR submissions feed and
/// the report is attributed to the registrant's <see cref="CommonStock"/> — <see cref="CommonStockId"/>
/// is set, <see cref="RegistrantCik"/> is null. The giant multi-series fund-family trusts
/// ("Vanguard Index Funds", "Fidelity Concord Street Trust", "iShares Trust") are not tracked
/// issuers, so they are instead discovered by the daily-index NPORT-P sweep — <see cref="CommonStockId"/>
/// is null and the registrant is identified by <see cref="RegistrantCik"/>. Exactly one of the two
/// is set, so the two populations never share a series.
///
/// The record captures the series' header facts — name, identifier, reporting period, total assets
/// and liabilities, net assets — plus the schedule of portfolio investments in
/// <see cref="Holdings"/>. Both the original report ("NPORT-P") and its amendments ("NPORT-P/A")
/// are stored, flagged via <see cref="IsAmendment"/>.
/// </summary>
[Index(nameof(CommonStockId), nameof(FilingDate))]
[Index(nameof(AccessionNumber), IsUnique = true)]
[Index(nameof(FilingDate))]
[Index(nameof(ParserVersion))]
[Index(nameof(RegistrantCik))]
public class NportFiling
{
    /// <summary>
    /// Current holdings-parsing algorithm version. Bump this whenever the NPORT-P parse changes so
    /// the reprocess worker re-derives every filing's <see cref="Holdings"/> from EDGAR. Version 1
    /// is the first that reads the portfolio schedule from the correct <c>formData</c> element.
    /// </summary>
    public const int CurrentParserVersion = 1;

    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The tracked stock the report is attributed to, when the fund is itself a tracked issuer
    /// (listed closed-end fund or standalone ETF trust). Null for filings discovered by the
    /// daily-index sweep, whose registrant is a fund-family trust that is not a tracked stock —
    /// those are identified by <see cref="RegistrantCik"/> instead.
    /// </summary>
    public Guid? CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    /// <summary>
    /// The registrant's SEC CIK, set on filings discovered by the daily-index NPORT-P sweep (where
    /// the registrant is not a tracked stock). Null on filings crawled through a tracked issuer's
    /// submissions feed, whose registrant is identified by <see cref="CommonStockId"/>. Used both to
    /// re-fetch the submission during reprocess and to scope a series to its registrant.
    /// </summary>
    [MaxLength(16)]
    public string RegistrantCik { get; set; }

    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    public DateOnly FilingDate { get; set; }

    /// <summary>True when the submission type is "NPORT-P/A" (an amendment) rather than "NPORT-P".</summary>
    public bool IsAmendment { get; set; }

    /// <summary>The registrant's legal name exactly as reported on the filing.</summary>
    [MaxLength(512)]
    public string RegistrantName { get; set; }

    /// <summary>The fund series' name, e.g. "Vanguard 500 Index Fund".</summary>
    [MaxLength(512)]
    public string SeriesName { get; set; }

    /// <summary>The series' SEC identifier, e.g. "S000002277".</summary>
    [MaxLength(32)]
    public string SeriesId { get; set; }

    /// <summary>The series' Legal Entity Identifier when reported.</summary>
    [MaxLength(32)]
    public string SeriesLei { get; set; }

    /// <summary>The date the reported portfolio is as of (the report's "as of" date).</summary>
    public DateOnly ReportPeriodDate { get; set; }

    /// <summary>The last day of the fiscal period the monthly report belongs to.</summary>
    public DateOnly ReportPeriodEnd { get; set; }

    /// <summary>Total assets of the series in U.S. dollars.</summary>
    public decimal TotalAssets { get; set; }

    /// <summary>Total liabilities of the series in U.S. dollars.</summary>
    public decimal TotalLiabilities { get; set; }

    /// <summary>Net assets of the series in U.S. dollars (assets less liabilities).</summary>
    public decimal NetAssets { get; set; }

    /// <summary>True when the registrant marked this as the series' final NPORT-P report.</summary>
    public bool IsFinalFiling { get; set; }

    /// <summary>The series' schedule of portfolio investments reported on the filing.</summary>
    public virtual List<NportHolding> Holdings { get; set; } = [];

    /// <summary>
    /// Parsing-algorithm version that produced this filing's <see cref="Holdings"/>. See
    /// <see cref="CurrentParserVersion"/>. Defaults to 0 for filings imported before versioning,
    /// which marks them for reprocessing.
    /// </summary>
    public int ParserVersion { get; set; }

    /// <summary>
    /// Number of times the reprocess pass has failed to fetch or re-parse this filing. Once it
    /// reaches the reprocess ceiling the filing is advanced to <see cref="CurrentParserVersion"/>
    /// regardless, so a permanently-unfetchable filing (e.g. a delisted issuer's pulled submission)
    /// can't keep the backlog from draining.
    /// </summary>
    public int ReprocessAttempts { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
