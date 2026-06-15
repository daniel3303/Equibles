namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// Outcome of one <see cref="Services.HoldingsImportService.ImportDataSet"/> call.
/// <paramref name="InsertedHoldings"/> is the number of holding rows the import
/// actually upserted — the real-time path treats a non-amendment original that
/// imported as "complete" yet inserted zero holdings as suspect (it does NOT
/// record it processed, so a later cycle retries) rather than silently
/// consuming the filing forever.
/// </summary>
public record ImportResult(int SubmissionCount, bool IsComplete, int InsertedHoldings = 0);
