using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// One fund (series) of a <see cref="NCenRegistrantReport"/>: its N-CEN fund types, index details,
/// average net assets and advisers. When an amendment restates the same fund for the same period,
/// the older row is kept and marked <see cref="IsSuperseded"/>.
/// </summary>
[Index(nameof(FundId), IsUnique = true)]
[Index(nameof(SeriesId), nameof(ReportEndingPeriod))]
[Index(nameof(RegistrantCik), nameof(ReportEndingPeriod), nameof(SeriesId))]
[Index(nameof(RegistrantReportId))]
public class NCenFundReport
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RegistrantReportId { get; set; }
    public virtual NCenRegistrantReport RegistrantReport { get; set; }

    /// <summary>SEC's fund identifier: accession number, registrant CIK and series id.</summary>
    [MaxLength(64)]
    public string FundId { get; set; }

    /// <summary>The fund's series id, e.g. "S000002277"; null for a registrant without series.</summary>
    [MaxLength(16)]
    public string SeriesId { get; set; }

    [MaxLength(512)]
    public string FundName { get; set; }

    [MaxLength(20)]
    public string FundLei { get; set; }

    /// <summary>The registrant's CIK without leading zeros, copied from the report.</summary>
    [MaxLength(16)]
    public string RegistrantCik { get; set; }

    [MaxLength(20)]
    public string AccessionNumber { get; set; }

    public DateOnly FilingDate { get; set; }

    public DateOnly ReportEndingPeriod { get; set; }

    public NCenFundTypes FundTypes { get; set; }

    /// <summary>Item C.3.b.i.1: whether an affiliated person of the fund constructs its index.</summary>
    public bool? IsIndexAffiliated { get; set; }

    /// <summary>Item C.3.b.i.2: whether the index is constructed exclusively for the fund.</summary>
    public bool? IsIndexExclusive { get; set; }

    /// <summary>Item C.19.a: monthly average net assets (funds other than money market funds).</summary>
    public decimal? MonthlyAverageNetAssets { get; set; }

    /// <summary>Item C.19.b: daily average net assets (money market funds).</summary>
    public decimal? DailyAverageNetAssets { get; set; }

    /// <summary>True when a later filing restates this fund for the same registrant and period.</summary>
    public bool IsSuperseded { get; set; }

    public virtual List<NCenFundAdviser> Advisers { get; set; } = [];

    public virtual List<NCenFundShareClass> ShareClasses { get; set; } = [];

    public virtual List<NCenFundExchangeListing> ExchangeListings { get; set; } = [];
}
