using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// One Form N-CEN annual report (or its amendment) of any registered investment company, from
/// SEC's quarterly N-CEN data sets. Unlike <see cref="NCenFiling"/>, which covers tracked issuers
/// only, this covers every registrant, and its <see cref="Funds"/> carry the fund types and
/// adviser identifiers of Part C and Part D.
/// </summary>
[Index(nameof(AccessionNumber), IsUnique = true)]
[Index(nameof(RegistrantCik), nameof(ReportEndingPeriod))]
[Index(nameof(DataSetYear), nameof(DataSetQuarter))]
public class NCenRegistrantReport
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(20)]
    public string AccessionNumber { get; set; }

    /// <summary>The registrant's CIK without leading zeros.</summary>
    [MaxLength(16)]
    public string RegistrantCik { get; set; }

    [MaxLength(512)]
    public string RegistrantName { get; set; }

    /// <summary>The registrant's Investment Company Act file number, e.g. "811-02652".</summary>
    [MaxLength(32)]
    public string InvestmentCompanyFileNumber { get; set; }

    [MaxLength(20)]
    public string RegistrantLei { get; set; }

    /// <summary>The registration form of the registrant as filed, e.g. "N-1A" or "N-2".</summary>
    [MaxLength(16)]
    public string InvestmentCompanyType { get; set; }

    public DateOnly FilingDate { get; set; }

    public DateOnly ReportEndingPeriod { get; set; }

    public bool IsAmendment { get; set; }

    /// <summary>Item B.5: whether the registrant is part of a family of investment companies.</summary>
    public bool? IsFamilyInvestmentCompany { get; set; }

    [MaxLength(256)]
    public string FamilyInvestmentCompanyName { get; set; }

    /// <summary>Series the registrant reports in total, as filed.</summary>
    public int? TotalSeries { get; set; }

    /// <summary>Year of the SEC data set this report was imported from.</summary>
    public int DataSetYear { get; set; }

    public int DataSetQuarter { get; set; }

    public virtual List<NCenFundReport> Funds { get; set; } = [];
}
