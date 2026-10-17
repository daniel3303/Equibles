using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>A share class of a <see cref="NCenFundReport"/> (Item C.2), with its ticker when it has one.</summary>
[Index(nameof(FundReportId))]
[Index(nameof(Ticker))]
public class NCenFundShareClass
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FundReportId { get; set; }
    public virtual NCenFundReport FundReport { get; set; }

    /// <summary>SEC's class id, e.g. "C000012179".</summary>
    [MaxLength(16)]
    public string ClassId { get; set; }

    [MaxLength(256)]
    public string ClassName { get; set; }

    [MaxLength(16)]
    public string Ticker { get; set; }
}
