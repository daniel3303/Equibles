using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InvestorRelations.Data.Models;

// A press release / news item scraped from a company's investor relations page.
// The (CommonStockId, Url) pair is the natural key: the same article always lives at
// the same canonical URL, so re-scraping is idempotent.
[Index(nameof(CommonStockId), nameof(Url), IsUnique = true)]
[Index(nameof(PublishedDate))]
public class IrNewsItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    [Required]
    [MaxLength(512)]
    public string Title { get; set; }

    [Required]
    [MaxLength(512)]
    public string Url { get; set; }

    [MaxLength(4000)]
    public string Summary { get; set; }

    public DateOnly PublishedDate { get; set; }

    // The IR platform the item was scraped from, so multi-source rows stay attributable.
    public IrPlatformType Source { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
