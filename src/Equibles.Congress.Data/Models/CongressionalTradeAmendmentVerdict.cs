using System.ComponentModel.DataAnnotations;

namespace Equibles.Congress.Data.Models;

public enum CongressionalTradeAmendmentVerdict
{
    [Display(Name = "Pending")]
    Pending = 0,

    [Display(Name = "Matched")]
    Matched = 1,

    // No earlier filing of the member holds the row yet; retried every cycle because the original
    // may still be unread or a legacy row awaiting source adoption.
    [Display(Name = "No predecessor")]
    NoPredecessor = 2,

    // Several earlier filings explain the amendment, or its rows cannot pair one-to-one; nothing
    // is superseded because picking one would guess. Retried every cycle, reported once.
    [Display(Name = "Ambiguous")]
    Ambiguous = 3,
}
