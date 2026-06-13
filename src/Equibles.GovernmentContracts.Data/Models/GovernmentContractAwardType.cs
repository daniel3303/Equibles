using System.ComponentModel.DataAnnotations;

namespace Equibles.GovernmentContracts.Data.Models;

/// <summary>
/// The USAspending contract award type, mapped from the federal procurement
/// award_type_codes A/B/C/D. Grants, loans and other assistance are out of scope —
/// this module tracks procurement contracts only.
/// </summary>
public enum GovernmentContractAwardType
{
    [Display(Name = "Unknown")]
    Unknown = 0,

    [Display(Name = "BPA Call")]
    BpaCall = 1,

    [Display(Name = "Purchase Order")]
    PurchaseOrder = 2,

    [Display(Name = "Delivery Order")]
    DeliveryOrder = 3,

    [Display(Name = "Definitive Contract")]
    DefinitiveContract = 4,
}
