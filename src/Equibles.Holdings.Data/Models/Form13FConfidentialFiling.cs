using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// A 13F holdings report whose summary page declares positions omitted under a confidential
/// treatment request. A filer that withholds its whole book stores no holdings for the quarter,
/// so this row is the only evidence that it filed and that a missing position is not a sale.
/// </summary>
/// <remarks>
/// Most such reports omit a few positions and disclose the rest. Only one with
/// <see cref="DisclosedPositionCount"/> zero withholds the whole book.
/// </remarks>
[Index(nameof(AccessionNumber), IsUnique = true)]
[Index(nameof(FilerCik), nameof(ReportDate))]
public class Form13FConfidentialFiling
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    /// <summary>The filer's CIK without leading zeros, as <see cref="InstitutionalHolder.Cik"/> stores it.</summary>
    [Required]
    [MaxLength(16)]
    public string FilerCik { get; set; }

    /// <summary>The period of report as filed.</summary>
    public DateOnly ReportDate { get; set; }

    public DateOnly FilingDate { get; set; }
    public bool IsAmendment { get; set; }

    /// <summary>The summary page's declared row count, which counts the omitted positions too.</summary>
    public int? DeclaredEntryTotal { get; set; }

    /// <summary>Information-table rows that report a positive quantity or value.</summary>
    public int DisclosedPositionCount { get; set; }
}
