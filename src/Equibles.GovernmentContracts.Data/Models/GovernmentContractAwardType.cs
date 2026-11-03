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

    /// <summary>A Government of Canada contract (instrument type C).</summary>
    [Display(Name = "Contract")]
    Contract = 5,

    /// <summary>A Government of Canada call-up against a standing offer or supply arrangement (SOSA).</summary>
    [Display(Name = "Standing Offer Call-up")]
    StandingOfferCallUp = 6,
}
