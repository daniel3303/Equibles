using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// Who holds one listed security at a closed 13F quarter end, measured across every filer's Form
/// 13F book in the holder-intelligence rebuild: conviction, crowding, tenure, concentration,
/// estimated cost, active holders' like-for-like change, top-scored managers' net buying and the
/// Schedule 13D and 13G filers on record. Share counts are as filed, on the quarter's basis.
/// </summary>
/// <remarks>
/// Derived and recomputed with its quarter, so the issuer id carries no foreign key and an issuer
/// merge keeps the survivor's row.
/// </remarks>
[Index(nameof(ReportDate))]
public class StockQuarterlyHolderSignals
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquityIssuerId { get; set; }

    /// <summary>Null for the issuer's primary listing, as on the holding row.</summary>
    [MaxLength(32)]
    public string ListedTicker { get; set; }

    public DateOnly ReportDate { get; set; }

    /// <summary>13F filers holding common shares of the security.</summary>
    public int Filers { get; set; }

    public long Shares { get; set; }

    /// <summary>The value of the positions that carry one; a pending value adds nothing.</summary>
    public long Value { get; set; }

    /// <summary>The shares of the valued positions, so value over these is the quarter-end price.</summary>
    public long ValuedShares { get; set; }

    /// <summary>Filers with the issuer among their ten largest issuers by value.</summary>
    public int ConvictionHolders { get; set; }

    /// <summary>Filers with at least 5% of their valued book in the security.</summary>
    public int HoldersAboveFivePercent { get; set; }

    /// <summary>The mean of the filers' portfolio weights in the security.</summary>
    [Precision(12, 6)]
    public decimal? AverageWeight { get; set; }

    [Precision(12, 6)]
    public decimal? MedianWeight { get; set; }

    /// <summary>
    /// Shares held by filers measured high-turnover or advising private funds (Form ADV), the
    /// holders most likely to sell together.
    /// </summary>
    public long CrowdedShares { get; set; }

    public int CrowdedHolders { get; set; }

    /// <summary>Shares held by filers that advise private funds, by their Form ADV.</summary>
    public long PrivateFundAdviserShares { get; set; }

    /// <summary>
    /// Crowded shares over shares outstanding, ranked against every security of the quarter with a
    /// consolidated count, from 0 to 100; null where the count cannot divide this listing.
    /// </summary>
    [Precision(8, 4)]
    public decimal? CrowdingPercentile { get; set; }

    /// <summary>Value held for under 4 quarters.</summary>
    public long TenureShortValue { get; set; }

    /// <summary>Value held for 4 to 11 quarters.</summary>
    public long TenureMediumValue { get; set; }

    /// <summary>Value held for 12 quarters or more.</summary>
    public long TenureLongValue { get; set; }

    /// <summary>
    /// Value held since before the corpus starts for under 12 measured quarters, so how long is
    /// unknown.
    /// </summary>
    public long TenureCensoredValue { get; set; }

    [Precision(10, 4)]
    public decimal? ValueWeightedTenureQuarters { get; set; }

    /// <summary>The Herfindahl index of the filers' shares of the 13F shares, from 0 to 1.</summary>
    [Precision(12, 6)]
    public decimal? Herfindahl { get; set; }

    /// <summary>The ten largest filers' share of the 13F shares, from 0 to 1.</summary>
    [Precision(12, 6)]
    public decimal? TopTenHolderShare { get; set; }

    /// <summary>The shares whose cost could be estimated.</summary>
    public long PricedShares { get; set; }

    /// <summary>The value of the positions whose cost could be estimated.</summary>
    public long PricedValue { get; set; }

    /// <summary>The share-weighted estimated cost per share of the costed positions.</summary>
    [Precision(20, 6)]
    public decimal? EstimatedCostPerShare { get; set; }

    /// <summary>
    /// The costed value whose estimated cost is above the quarter-end price, over all costed
    /// value, from 0 to 1.
    /// </summary>
    [Precision(12, 6)]
    public decimal? UnderwaterValueShare { get; set; }

    /// <summary>
    /// Like-for-like shares of filers measured active (systematic-like, concentrated or
    /// diversified active) that filed both quarters, this quarter; null with none compared.
    /// </summary>
    public long? ActiveLikeForLikeShares { get; set; }

    /// <summary>The same filers' shares the quarter before, restated onto this quarter's basis.</summary>
    public long? ActiveLikeForLikePreviousShares { get; set; }

    /// <summary>
    /// Buy minus sell value of managers in the top fifth by current three-year alpha; computed for
    /// the newest closed quarter only, since the score is current, and null elsewhere.
    /// </summary>
    [Precision(20, 2)]
    public decimal? TopScoreNetBuyValue { get; set; }

    public int? TopScoreBuyers { get; set; }
    public int? TopScoreSellers { get; set; }

    /// <summary>The cover-page share count dated on or before the quarter end, on its basis.</summary>
    public long? SharesOutstanding { get; set; }

    public DateOnly? SharesOutstandingAsOf { get; set; }
    public SharesOutstandingBasis SharesOutstandingBasis { get; set; }

    /// <summary>
    /// Insiders' shares from their newest Form 3, 4 or 5 within two years of the quarter end,
    /// excluding insiders that file 13F books; null unless the count is consolidated and this is
    /// the primary listing, since insider balances are issuer-wide.
    /// </summary>
    public long? InsiderShares { get; set; }

    /// <summary>Schedule 13D filers whose newest filing by the quarter end reports a stake.</summary>
    public int Schedule13DFilers { get; set; }

    public int Schedule13GFilers { get; set; }

    /// <summary>13G filers under Rule 13d-1(b), qualified institutions.</summary>
    public int Schedule13GQualifiedInstitutions { get; set; }

    /// <summary>13G filers under Rule 13d-1(c), passive investors.</summary>
    public int Schedule13GPassiveInvestors { get; set; }

    /// <summary>13G filers under Rule 13d-1(d), exempt investors.</summary>
    public int Schedule13GExemptInvestors { get; set; }
}
