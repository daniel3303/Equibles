using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.EquityMarkets.Data.Models;

// One FIRDS reference-data row (ISIN on a venue) as the authority last published it; never a listing.
[PrimaryKey(nameof(Authority), nameof(Isin), nameof(Mic))]
[Index(nameof(Authority), nameof(RelevantTradingVenue))]
[Index(nameof(Lei))]
[Index(nameof(Isin))]
public class FirdsInstrumentRecord
{
    [Required, MaxLength(4)]
    public string Authority { get; set; }

    [Required, MaxLength(12)]
    public string Isin { get; set; }

    [Required, MaxLength(4)]
    public string Mic { get; set; }

    [MaxLength(20)]
    public string Lei { get; set; }

    [Required, MaxLength(6)]
    public string Cfi { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; }

    [MaxLength(350)]
    public string FullName { get; set; }

    [MaxLength(35)]
    public string ShortName { get; set; }

    public DateTime? FirstTradeDate { get; set; }
    public DateTime? TerminationDate { get; set; }

    [MaxLength(2)]
    public string RelevantCompetentAuthority { get; set; }

    [MaxLength(4)]
    public string RelevantTradingVenue { get; set; }

    public DateTime ObservedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
}
