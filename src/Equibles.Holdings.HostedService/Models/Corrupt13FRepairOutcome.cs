namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// Counters from repairing a filing whose share-count column duplicated its
/// value column: rows whose share count was recovered, and rows dropped
/// because no repair anchor (closing price or voting-authority total) existed.
/// </summary>
public record Corrupt13FRepairOutcome(int RepairedRows, int DroppedRows);
