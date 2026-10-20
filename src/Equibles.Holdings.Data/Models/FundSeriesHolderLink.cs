using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The 13F filer whose report holds a registered fund series' positions: the series' latest N-CEN
/// names exactly one investment adviser CRD, that CRD identifies one filer (its Form ADV profile
/// or a CRD filed on a 13F notice or other-manager list), and the link follows that filer's 13F
/// reporting chain to its top. Sub-advisers never count; a series with several advisers has no row.
/// </summary>
[Index(nameof(InstitutionalHolderId))]
public class FundSeriesHolderLink
{
    [Key]
    [MaxLength(16)]
    public string SeriesId { get; set; }

    [MaxLength(16)]
    public string RegistrantCik { get; set; }

    public int AdviserCrd { get; set; }

    public Guid InstitutionalHolderId { get; set; }

    public FundSeriesHolderLinkBasis Basis { get; set; }

    public DateOnly NCenReportEndingPeriod { get; set; }
}
