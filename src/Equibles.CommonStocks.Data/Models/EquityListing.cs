using System.ComponentModel.DataAnnotations;
using Equibles.Data.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

// Stable identity for a security traded on a venue, independent of any public URL.
public class EquityListing : IActivable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquitySecurityId { get; set; }
    public virtual EquitySecurity Security { get; set; }

    [MaxLength(4)]
    public string MarketIdentifierCode { get; set; }

    // The venue's country, independently of the issuer's domicile or quotation currency.
    [MaxLength(2)]
    public string MarketCountryCode { get; set; }

    [Required, MaxLength(32)]
    public string Ticker { get; set; }

    [MaxLength(3)]
    public string TradingCurrency { get; set; }

    // Multiply the source quote by this value to obtain major currency units.
    // Required evidence: do not default an unknown quotation scale to one.
    [Precision(18, 8)]
    public decimal? QuoteUnitMultiplier { get; set; }
    public EquityIdentityState IdentityState { get; set; }

    public bool Active { get; set; } = true;
    public DateOnly? ListedOn { get; set; }
    public DateOnly? DelistedOn { get; set; }

    public bool IsDirectoryListed { get; set; }
    public bool IsReferenceListed { get; set; }
    public bool PriceHistoryBackfilled { get; set; }
    public DateTime? YahooEnrichmentAttemptedAt { get; set; }
    public DateTime? HistoricalPriceBackfillAttemptedAt { get; set; }
    public DateTime? HistoricalCusipBackfillRequestedAt { get; set; }
    public List<string> HistoricalCusipBackfillCandidates { get; set; } = [];
    public DateOnly? HistoricalCusipBackfillCandidateOn { get; set; }
    public bool HistoricalCusipBackfillAmbiguous { get; set; }
    public DateTime? HistoricalCusipBackfillSweepStartedAt { get; set; }

    [MaxLength(2000)]
    public string IdentitySourceUrl { get; set; }
}
