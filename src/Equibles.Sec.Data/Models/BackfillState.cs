using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// One row per backfill cursor, keyed by the worker-assigned cursor name. Persists the
/// frontier and the last unfloored-rescan stamp so a process restart neither re-scans the
/// corpus to find its position nor forgets when the full rescan last ran — deploy bursts
/// would otherwise repeat the minutes-long corpus scan on every boot, or dodge the daily
/// backstop indefinitely.
/// </summary>
public class BackfillState
{
    [Key]
    [MaxLength(100)]
    public string Name { get; set; }

    /// <summary>Backfill frontier (mirrors BackfillCursor.Floor); null until a batch advances it.</summary>
    public DateTime? Floor { get; set; }

    /// <summary>When the last unfloored full rescan started; null until the first one runs.</summary>
    public DateTime? LastFullRescanAt { get; set; }
}
