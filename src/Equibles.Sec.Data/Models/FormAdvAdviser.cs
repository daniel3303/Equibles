using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// An investment adviser as reported on SEC Form ADV Part 1A. The SEC publishes the full
/// population of registered advisers and exempt reporting advisers as a monthly bulk download
/// (one CSV row per firm). Unlike most SEC filings this data is not tied to a stock issuer —
/// each firm is keyed by its Organization CRD number, the stable identifier FINRA/IARD assigns
/// when the firm first enters the system. The federal SEC file number (the "801-…" / "802-…"
/// value) is kept as a secondary attribute and is absent for state-registered firms.
/// </summary>
[Index(nameof(Crd), IsUnique = true)]
[Index(nameof(LegalName))]
[Index(nameof(TotalRegulatoryAum))]
public class FormAdvAdviser
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Organization CRD number — the adviser's stable primary identifier.</summary>
    public int Crd { get; set; }

    /// <summary>SEC file number, e.g. "801-54739" (federal IA) or "802-…" (exempt reporting adviser); null for state-only firms.</summary>
    [MaxLength(32)]
    public string SecNumber { get; set; }

    /// <summary>The firm's legal name exactly as reported (Form ADV Item 1.A).</summary>
    [MaxLength(512)]
    public string LegalName { get; set; }

    /// <summary>The primary business / "doing-business-as" name when it differs from the legal name (Item 1.B).</summary>
    [MaxLength(512)]
    public string PrimaryBusinessName { get; set; }

    /// <summary>City of the firm's main office (Item 1.F).</summary>
    [MaxLength(128)]
    public string MainOfficeCity { get; set; }

    /// <summary>State or province of the firm's main office (Item 1.F).</summary>
    [MaxLength(128)]
    public string MainOfficeState { get; set; }

    /// <summary>Country of the firm's main office (Item 1.F).</summary>
    [MaxLength(128)]
    public string MainOfficeCountry { get; set; }

    /// <summary>The firm's public website when disclosed (Item 1.I).</summary>
    [MaxLength(512)]
    public string WebsiteAddress { get; set; }

    /// <summary>The firm's reported registration status with the SEC, e.g. "Approved"; null when not provided.</summary>
    [MaxLength(64)]
    public string SecStatus { get; set; }

    /// <summary>Number of employees who perform investment advisory functions (Item 5.A); null when not reported.</summary>
    public int? NumberOfEmployees { get; set; }

    /// <summary>Total regulatory assets under management in US dollars (Item 5.F(2)(c)); null when not reported.</summary>
    public long? TotalRegulatoryAum { get; set; }

    /// <summary>Regulatory assets under management held on a discretionary basis (Item 5.F(2)(a)); null when not reported.</summary>
    public long? DiscretionaryAum { get; set; }

    /// <summary>Regulatory assets under management held on a non-discretionary basis (Item 5.F(2)(b)); null when not reported.</summary>
    public long? NonDiscretionaryAum { get; set; }

    /// <summary>Charges fees as a percentage of assets under management (Item 5.E(1)).</summary>
    public bool ChargesPercentageOfAum { get; set; }

    /// <summary>Charges hourly fees (Item 5.E(2)).</summary>
    public bool ChargesHourly { get; set; }

    /// <summary>Charges subscription fees (Item 5.E(3)).</summary>
    public bool ChargesSubscription { get; set; }

    /// <summary>Charges fixed fees other than subscription fees (Item 5.E(4)).</summary>
    public bool ChargesFixed { get; set; }

    /// <summary>Charges commissions (Item 5.E(5)).</summary>
    public bool ChargesCommissions { get; set; }

    /// <summary>Charges performance-based fees (Item 5.E(6)).</summary>
    public bool ChargesPerformanceBased { get; set; }

    /// <summary>Charges other types of compensation (Item 5.E(7)).</summary>
    public bool ChargesOther { get; set; }

    /// <summary>The snapshot date of the bulk file this record was last imported from.</summary>
    public DateOnly ReportDate { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
}
