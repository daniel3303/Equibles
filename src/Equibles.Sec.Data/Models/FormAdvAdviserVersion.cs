using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// One stretch of monthly Form ADV rosters over which an adviser's tracked roster content stayed
/// the same. A roster adds a row only when the content hash changes or the adviser returns after
/// missing a roster; otherwise it moves <see cref="LastSeenReportDate"/> forward. The content is
/// valid at a date when the newest roster of its kind on or before that date lies between
/// <see cref="ReportDate"/> and <see cref="LastSeenReportDate"/>.
/// <para>
/// Tracked (hashed) content: SEC file number, legal name, CIK, LEI, organization form,
/// employees, regulatory assets (Item 5.F), Items 2.A, 2.B, 6.A and 7.A, private funds (Item 7.B
/// with Schedule D 7.B.(1) counts and gross assets) and the Item 5.D client types in
/// <see cref="ClientTypes"/>. <see cref="LatestFilingDate"/> is kept from the first roster of the
/// stretch but not hashed, since a new filing often changes nothing tracked.
/// </para>
/// <para>
/// Registered-adviser history starts with the December 2019 roster; SEC publishes exempt
/// reporting adviser rosters only from February 2024.
/// </para>
/// </summary>
[Index(nameof(Crd), nameof(Registration), nameof(ReportDate), IsUnique = true)]
[Index(nameof(Registration), nameof(ReportDate))]
public class FormAdvAdviserVersion
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Organization CRD number of the adviser.</summary>
    public int Crd { get; set; }

    /// <summary>The roster this stretch was read from.</summary>
    public FormAdvRegistration Registration { get; set; }

    /// <summary>The first roster with this content.</summary>
    public DateOnly ReportDate { get; set; }

    /// <summary>The last roster that listed the adviser with this content.</summary>
    public DateOnly LastSeenReportDate { get; set; }

    /// <summary>SHA-256 of the tracked content, lowercase hex.</summary>
    [MaxLength(64)]
    public string ContentHash { get; set; }

    [MaxLength(32)]
    public string SecNumber { get; set; }

    [MaxLength(512)]
    public string LegalName { get; set; }

    [MaxLength(16)]
    public string Cik { get; set; }

    [MaxLength(20)]
    public string Lei { get; set; }

    public FormAdvOrganizationForm? OrganizationForm { get; set; }

    public int? NumberOfEmployees { get; set; }

    public long? TotalRegulatoryAum { get; set; }

    public long? DiscretionaryAum { get; set; }

    public long? NonDiscretionaryAum { get; set; }

    public FormAdvRegistrationBasis RegistrationBasis { get; set; }

    public FormAdvExemptionBasis ExemptionBasis { get; set; }

    public FormAdvOtherBusiness OtherBusinesses { get; set; }

    public FormAdvRelatedPerson RelatedPersons { get; set; }

    public bool? AdvisesPrivateFunds { get; set; }

    public int? PrivateFundCount { get; set; }

    public int? HedgeFundCount { get; set; }

    public int? PrivateEquityFundCount { get; set; }

    public int? VentureCapitalFundCount { get; set; }

    public int? RealEstateFundCount { get; set; }

    public int? LiquidityFundCount { get; set; }

    public int? SecuritizedAssetFundCount { get; set; }

    public int? OtherPrivateFundCount { get; set; }

    public long? PrivateFundGrossAssets { get; set; }

    /// <summary>Filing date of the adviser's latest Form ADV on the first roster of the stretch.</summary>
    public DateOnly? LatestFilingDate { get; set; }

    public virtual List<FormAdvClientTypeAum> ClientTypes { get; set; } = [];
}
