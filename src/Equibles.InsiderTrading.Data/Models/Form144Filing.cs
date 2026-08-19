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
[Index(nameof(FilerCik), nameof(FilingDate))]
public class Form144Filing
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    public DateOnly FilingDate { get; set; }

    /// <summary>
    /// Person for whose account the securities are to be sold (the affiliate), as free text.
    /// Use <see cref="FilerCik"/> to identify them; names are not unique and not stable.
    /// </summary>
    [MaxLength(512)]
    public string SellerName { get; set; }

    /// <summary>
    /// CIK of the natural person who filed the notice, taken from the submission's
    /// <c>filerCredentials</c> block. This is the same identifier that person's Forms 3/4/5
    /// are filed under, so it joins directly to <see cref="InsiderOwner.OwnerCik"/> and is the
    /// only reliable way to tell whether a proposed sale was ever executed.
    ///
    /// Stored verbatim, zero-padded to ten characters exactly as EDGAR emits it and exactly as
    /// <see cref="InsiderOwner.OwnerCik"/> stores it, so the two compare without normalisation.
    ///
    /// Null on notices imported before this field was captured; the backfill fills them in.
    /// </summary>
    [MaxLength(16)]
    public string FilerCik { get; set; }

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

    /// <summary>
    /// Adoption date of the Rule 10b5-1 trading plan the sale is made under, when the notice
    /// declares one. Its presence is the notice's own signal that the sale is pre-arranged
    /// rather than discretionary.
    ///
    /// This matters most where the Form 4 side cannot help: an executed notice can borrow
    /// <see cref="InsiderTransaction.IsRule10b5One"/> from the matching transaction, but a
    /// notice that is never executed has no transaction to borrow from.
    ///
    /// Null when the notice declares no plan, and on notices imported before this field was
    /// captured. The two are not distinguishable until the backfill has drained.
    /// </summary>
    public DateOnly? PlanAdoptionDate { get; set; }

    public virtual List<Form144PriorSale> PriorSales { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
