using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Data.Models;

/// <summary>
/// A SEC Form 144 notice — an affiliate's declaration of intent to sell restricted or
/// control securities. Filed under the issuer's submissions feed, so each notice is
/// attributed to the issuer's <see cref="CommonStock"/>. Unlike a Form 4, this records a
/// <em>proposed</em> sale (shares, aggregate market value, approximate sale date), not an
/// executed transaction.
/// </summary>
[Index(nameof(CommonStockId), nameof(FilingDate))]
[Index(nameof(AccessionNumber), IsUnique = true)]
[Index(nameof(FilingDate))]
public class Form144Filing
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    public DateOnly FilingDate { get; set; }

    /// <summary>
    /// Person for whose account the securities are to be sold (the affiliate). Form 144 XML
    /// carries the seller's name but not a CIK, so this is stored as free text rather than
    /// resolved to an <see cref="InsiderOwner"/>.
    /// </summary>
    [MaxLength(512)]
    public string SellerName { get; set; }

    /// <summary>
    /// The seller's relationship(s) to the issuer (e.g. "Director", "Officer"). A filing can
    /// list several — joined with ", ".
    /// </summary>
    [MaxLength(256)]
    public string RelationshipToIssuer { get; set; }

    // ADR/foreign-issuer class titles are long legal descriptions (e.g. "American Depositary
    // Shares, each representing the right to receive one Share of Capital Stock of ..."), so this
    // is sized well beyond a plain ticker class to store them in full.
    [MaxLength(512)]
    public string SecurityClassTitle { get; set; }

    [MaxLength(256)]
    public string BrokerName { get; set; }

    public long SharesToBeSold { get; set; }

    public decimal AggregateMarketValue { get; set; }

    public long SharesOutstanding { get; set; }

    public DateOnly? ApproxSaleDate { get; set; }

    [MaxLength(64)]
    public string SecuritiesExchangeName { get; set; }

    [MaxLength(2048)]
    public string Remarks { get; set; }

    public virtual List<Form144PriorSale> PriorSales { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
