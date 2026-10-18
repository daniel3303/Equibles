using System.ComponentModel.DataAnnotations;

namespace Equibles.Congress.Data.Models;

/// <summary>How a <see cref="CongressMember.BioguideId"/> was established.</summary>
public enum CongressBioguideIdSource
{
    [Display(Name = "Reviewed catalog")]
    Catalog = 0,

    [Display(Name = "Seat and surname match")]
    SeatAndSurnameMatch = 1,
}
