using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// A run of days on which SEC's company ticker directory (<c>company_tickers_exchange.json</c>)
/// listed one ticker for one CIK on one exchange. The span stays open only while every capture
/// lists the same triple, so a changed exchange or a ticker that leaves and returns starts a new
/// row, and the directory's daily restatement becomes dated evidence of who held a ticker where.
/// </summary>
[Index(
    nameof(Cik),
    nameof(Ticker),
    nameof(Exchange),
    nameof(FirstObservedOn),
    IsUnique = true,
    Name = "IX_SecTickerDirectoryObservation_Span"
)]
[Index(nameof(Ticker), nameof(LastObservedOn))]
[Index(nameof(LastObservedOn))]
public class SecTickerDirectoryObservation
{
    /// <summary>The directory's exchange value for a ticker quoted over the counter, not listed on an exchange.</summary>
    public const string OverTheCounter = "OTC";

    public const int ExchangeMaxLength = 32;

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Unpadded, the form <see cref="CikNormalizer.Canonicalize"/> gives and <see cref="EquityIssuer.Cik"/> stores.</summary>
    [Required]
    [MaxLength(CikNormalizer.MaxLength)]
    public string Cik { get; set; }

    /// <summary>The <see cref="TickerNormalizer.NormalizeIdentity"/> form, the same one ticker evidence stores.</summary>
    [Required]
    [MaxLength(TickerNormalizer.MaxListedLength)]
    public string Ticker { get; set; }

    /// <summary>The exchange exactly as the directory states it.</summary>
    [Required]
    [MaxLength(ExchangeMaxLength)]
    public string Exchange { get; set; }

    public DateOnly FirstObservedOn { get; set; }

    public DateOnly LastObservedOn { get; set; }
}
