using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>How a <see cref="FundSeriesHolderLink"/> reaches its 13F filer from the series' adviser.</summary>
public enum FundSeriesHolderLinkBasis
{
    /// <summary>The adviser files the 13F report itself.</summary>
    [Display(Name = "Adviser's own 13F")]
    AdviserFiler = 0,

    /// <summary>The adviser's 13F holdings are reported by the filer at the top of its reporting chain.</summary>
    [Display(Name = "Adviser's 13F reporting parent")]
    ReportingParent = 1,
}
