using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Data.Models;

/// <summary>
/// One reading of an earnings release for statement figures: the processed marker, kept whether
/// or not the release yielded any, so the lane reads each release once per matcher version.
/// </summary>
[Index(nameof(DocumentId), IsUnique = true)]
public class ReleaseStatementRead
{
    /// <summary>
    /// Version of the matching rule. Raising it re-reads every release still inside the lane's
    /// window; a rule change that can place or withdraw a figure needs a new version.
    /// </summary>
    public const int CurrentMatcherVersion = 1;

    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquityIssuerId { get; set; }
    public virtual EquityIssuer Issuer { get; set; }

    public Guid DocumentId { get; set; }
    public virtual Document Document { get; set; }

    public int MatcherVersion { get; set; }

    public int FigureCount { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    public virtual List<ReleaseStatementFigure> Figures { get; set; } = [];
}
