namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>Resolved facts were saved, but other measured dates still lack a historical calendar.</summary>
public sealed class FiscalCalendarEvidencePendingException(int persistedCount, int deferredCount)
    : Exception(
        $"Historical calendar evidence is pending for {deferredCount} facts; {persistedCount} resolved facts were persisted"
    )
{
    public int PersistedCount { get; } = persistedCount;
    public int DeferredCount { get; } = deferredCount;
}
