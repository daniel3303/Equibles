using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

[PrimaryKey(nameof(Source), nameof(SourceRecordKey))]
public class EquityDirectorySnapshotState
{
    [Required, MaxLength(64)]
    public string Source { get; set; }

    [Required, MaxLength(256)]
    public string SourceRecordKey { get; set; }

    public Guid SourceRecordId { get; set; }
    public virtual EquityDirectorySourceRecord SourceRecord { get; set; }
    public DateTime ObservedAt { get; set; }
}
