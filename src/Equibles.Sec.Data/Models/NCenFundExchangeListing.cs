using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>An exchange listing of an exchange-traded <see cref="NCenFundReport"/> (Item E.1).</summary>
[Index(nameof(FundReportId))]
[Index(nameof(Ticker))]
public class NCenFundExchangeListing
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FundReportId { get; set; }
    public virtual NCenFundReport FundReport { get; set; }

    /// <summary>The exchange as filed, usually its MIC, e.g. "XNMS".</summary>
    [MaxLength(16)]
    public string Exchange { get; set; }

    [MaxLength(16)]
    public string Ticker { get; set; }
}
