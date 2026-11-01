using System.ComponentModel.DataAnnotations;

namespace Equibles.Congress.Data.Models;

/// <summary>
/// The per-row filing status a House periodic transaction report prints. An amending report lists
/// only the rows it changes: an amended row is the corrected copy of an earlier row, and a deleted
/// row reprints an earlier row to withdraw it.
/// </summary>
public enum CongressionalTradeFilingStatus
{
    [Display(Name = "Not stated")]
    Unstated = 0,

    [Display(Name = "New")]
    New = 1,

    [Display(Name = "Amended")]
    Amended = 2,

    [Display(Name = "Deleted")]
    Deleted = 3,
}
