using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// A single investor-relations news item (press release) for a company, scraped
/// from its IR website. The unique (CommonStockId, Url) index makes re-scraping
/// idempotent: the same release at the same URL is inserted once. <see cref="Source"/>
/// records which platform produced it so the same story carried by more than one
/// wire service can be reconciled when aggregating for display.
/// </summary>
[Index(nameof(CommonStockId), nameof(Url), IsUnique = true)]
[Index(nameof(CommonStockId), nameof(PublishedAt))]
public class IrNewsItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    [MaxLength(512)]
    public string Title { get; set; }

    [MaxLength(1024)]
    public string Url { get; set; }

    /// <summary>When the release was published, in UTC.</summary>
    public DateTime PublishedAt { get; set; }

    /// <summary>Short teaser / summary when the source provides one; otherwise null.</summary>
    [MaxLength(4000)]
    public string Summary { get; set; }

    /// <summary>The IR platform this item was scraped from.</summary>
    public IrPlatformType Source { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
