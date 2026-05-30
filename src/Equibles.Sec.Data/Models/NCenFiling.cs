using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// A SEC Form N-CEN annual report filed by a registered investment company (mutual fund, ETF or
/// closed-end fund). N-CEN appears in the registrant's EDGAR submissions feed, so each report is
/// attributed to the registrant's <see cref="CommonStock"/>. The record captures the registrant's
/// operational facts — classification, file number, reporting period — plus the fund's key service
/// providers (advisers, custodians, transfer agents, auditors and so on) in
/// <see cref="ServiceProviders"/>. Both the original report ("N-CEN") and its amendments
/// ("N-CEN/A") are stored, flagged via <see cref="IsAmendment"/>.
/// </summary>
[Index(nameof(CommonStockId), nameof(FilingDate))]
[Index(nameof(AccessionNumber), IsUnique = true)]
[Index(nameof(FilingDate))]
public class NCenFiling : IStockFiling
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    public DateOnly FilingDate { get; set; }

    /// <summary>True when the submission type is "N-CEN/A" (an amendment) rather than "N-CEN".</summary>
    public bool IsAmendment { get; set; }

    /// <summary>The registrant's legal name exactly as reported on the filing.</summary>
    [MaxLength(512)]
    public string RegistrantName { get; set; }

    /// <summary>
    /// The investment-company registration type, e.g. "N-1A" (open-end fund/ETF), "N-2"
    /// (closed-end fund), "N-3"/"N-4"/"N-6" (insurance products), "N-5" (small business).
    /// </summary>
    [MaxLength(16)]
    public string InvestmentCompanyType { get; set; }

    /// <summary>The Investment Company Act file number, e.g. "811-02409".</summary>
    [MaxLength(32)]
    public string InvestmentCompanyFileNumber { get; set; }

    /// <summary>The registrant's Legal Entity Identifier when reported.</summary>
    [MaxLength(32)]
    public string RegistrantLei { get; set; }

    /// <summary>The registrant's state or province code, e.g. "US-MD".</summary>
    [MaxLength(16)]
    public string State { get; set; }

    /// <summary>The registrant's country code, e.g. "US".</summary>
    [MaxLength(8)]
    public string Country { get; set; }

    /// <summary>The last day of the fiscal period the report covers.</summary>
    public DateOnly ReportEndingPeriod { get; set; }

    /// <summary>True when the report period covers fewer than twelve months.</summary>
    public bool IsReportPeriodLessThan12Months { get; set; }

    /// <summary>True when this is the registrant's first N-CEN filing.</summary>
    public bool IsFirstFiling { get; set; }

    /// <summary>True when the registrant marked this as its last filing (e.g. deregistration).</summary>
    public bool IsLastFiling { get; set; }

    /// <summary>True when the registrant is part of a fund family.</summary>
    public bool IsFamilyInvestmentCompany { get; set; }

    /// <summary>
    /// The advisers, custodians, transfer agents, auditors and other firms the fund named on the
    /// filing — the operational backbone behind the registrant.
    /// </summary>
    public virtual List<NCenServiceProvider> ServiceProviders { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
