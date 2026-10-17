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
[Index(nameof(Cik))]
[Index(nameof(Lei))]
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

    /// <summary>
    /// The roster the adviser was last listed in; null for a row not seen in a roster since the
    /// column existed.
    /// </summary>
    public FormAdvRegistration? Registration { get; set; }

    /// <summary>The adviser's SEC CIK (the roster's <c>CIK#</c>), without leading zeros; null when not reported.</summary>
    [MaxLength(16)]
    public string Cik { get; set; }

    /// <summary>How many CIKs the adviser reports; the roster lists only the first.</summary>
    public int? CikCount { get; set; }

    /// <summary>Legal Entity Identifier (Item 1.P); null unless the filed value passes the ISO 17442 check.</summary>
    [MaxLength(20)]
    public string Lei { get; set; }

    /// <summary>Filing date of the adviser's latest Form ADV.</summary>
    public DateOnly? LatestFilingDate { get; set; }

    /// <summary>How the adviser is organized (Item 3.A); null when not reported or unrecognized.</summary>
    public FormAdvOrganizationForm? OrganizationForm { get; set; }

    /// <summary>Why a registered adviser may register with the SEC (Item 2.A).</summary>
    public FormAdvRegistrationBasis RegistrationBasis { get; set; }

    /// <summary>Why an exempt reporting adviser is exempt (Item 2.B).</summary>
    public FormAdvExemptionBasis ExemptionBasis { get; set; }

    /// <summary>Other businesses the adviser is engaged in (Item 6.A).</summary>
    public FormAdvOtherBusiness OtherBusinesses { get; set; }

    /// <summary>Kinds of related persons the adviser has (Item 7.A).</summary>
    public FormAdvRelatedPerson RelatedPersons { get; set; }

    /// <summary>Advises at least one private fund (Item 7.B); null when not reported.</summary>
    public bool? AdvisesPrivateFunds { get; set; }

    /// <summary>Private funds the adviser reports on Schedule D 7.B.(1).</summary>
    public int? PrivateFundCount { get; set; }

    public int? HedgeFundCount { get; set; }

    public int? PrivateEquityFundCount { get; set; }

    public int? VentureCapitalFundCount { get; set; }

    public int? RealEstateFundCount { get; set; }

    public int? LiquidityFundCount { get; set; }

    public int? SecuritizedAssetFundCount { get; set; }

    public int? OtherPrivateFundCount { get; set; }

    /// <summary>Gross assets of the private funds reported on Schedule D 7.B.(1), in US dollars.</summary>
    public long? PrivateFundGrossAssets { get; set; }

    /// <summary>The snapshot date of the bulk file this record was last imported from.</summary>
    public DateOnly ReportDate { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
}
