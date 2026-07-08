using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Data.Models;

/// <summary>
/// One row of an issuer's SEC cover-page 12(b) registration table — the
/// authoritative pairing of a listed security's title, trading symbol and
/// exchange (<c>dei:Security12bTitle</c> / <c>dei:TradingSymbol</c> /
/// <c>dei:SecurityExchangeName</c>), extracted from the filing's raw XBRL
/// envelope. One row per (issuer, symbol); a newer filing's statement replaces
/// an older one, so each row always reflects the most recent filing that
/// mentioned the symbol. Rows whose symbol left the newest filing (a delisted
/// note) are kept — they remain the latest authoritative statement about that
/// symbol. Source of the per-ticker classification materialized on
/// <see cref="CommonStock.ListedSecurityType"/>.
/// </summary>
[Index(nameof(CommonStockId), nameof(TradingSymbol), IsUnique = true)]
public class ListedSecurity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    /// <summary>
    /// The filed <c>dei:TradingSymbol</c>, normalized for matching (uppercase,
    /// class separators removed — filings write "BRK.B" where ticker feeds use
    /// "BRK-B").
    /// </summary>
    [Required]
    [MaxLength(32)]
    public string TradingSymbol { get; set; }

    /// <summary>The <c>dei:Security12bTitle</c>, verbatim from the filing.</summary>
    [Required]
    [MaxLength(500)]
    public string Title { get; set; }

    /// <summary>The <c>dei:SecurityExchangeName</c> from the same context, when filed.</summary>
    [MaxLength(100)]
    public string ExchangeName { get; set; }

    /// <summary>Accession of the filing this row's statement came from.</summary>
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    /// <summary>Filing date of that filing — newer statements replace older ones.</summary>
    public DateOnly FiledDate { get; set; }
}
