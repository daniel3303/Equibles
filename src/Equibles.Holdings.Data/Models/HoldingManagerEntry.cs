using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

[Owned]
public class HoldingManagerEntry
{
    /// <summary>
    /// The summary-page sequence number this leg is credited to — the FIRST entry of the filed
    /// attribution, which can name several managers. Values below 1 mean the filer wrote a
    /// literal "none" (sequences are 1-based); consumers must treat them like null.
    /// </summary>
    public int? ManagerNumber { get; set; }

    [MaxLength(256)]
    public string ManagerName { get; set; }

    /// <summary>
    /// The raw filed attribution when it references MORE than one manager ("4,8,11" — investment
    /// discretion shared among them), else null. The leg's figures are credited to
    /// <see cref="ManagerNumber"/> alone, so this is what lets a surface say the position is
    /// jointly managed instead of presenting it as one manager's exclusive slice.
    /// </summary>
    [MaxLength(128)]
    public string SharedManagerNumbers { get; set; }

    public long Shares { get; set; }
    public long Value { get; set; }
    public InvestmentDiscretion InvestmentDiscretion { get; set; }
}
