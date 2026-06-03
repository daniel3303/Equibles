using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Data.Models;

/// <summary>
/// A sale of the issuer's securities by the same seller during the three months preceding a
/// <see cref="Form144Filing"/>. Form 144 requires these to be disclosed alongside the proposed
/// sale, and they are the key signal for how much the affiliate has already sold recently.
/// </summary>
[Index(nameof(Form144FilingId))]
public class Form144PriorSale
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid Form144FilingId { get; set; }
    public virtual Form144Filing Form144Filing { get; set; }

    [MaxLength(512)]
    public string SellerName { get; set; }

    // Matches Form144Filing.SecurityClassTitle — long ADR/foreign-issuer class descriptions.
    [MaxLength(512)]
    public string SecurityClassTitle { get; set; }

    public DateOnly? SaleDate { get; set; }

    public long AmountSold { get; set; }

    public decimal GrossProceeds { get; set; }
}
