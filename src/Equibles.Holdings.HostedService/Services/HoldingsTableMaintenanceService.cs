using Equibles.Core.AutoWiring;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Online maintenance for the InstitutionalHolding table after a 13F sweep.
///
/// Each sweep bulk-inserts rows, and Postgres leaves those new heap pages out of the visibility
/// map until a VACUUM runs. Until then the index-only scans behind the holdings dashboards fall
/// back to a heap fetch per row: a cold /holdings/activity read was measured at ~66s, dominated
/// by that random heap I/O over millions of not-yet-all-visible rows. Running VACUUM (ANALYZE)
/// right after a productive sweep restores true index-only scans and refreshes planner statistics
/// before the snapshot rebuild and user reads touch the new rows.
///
/// VACUUM cannot run inside a transaction block, so this takes its own scope/connection with no
/// ambient transaction. Plain VACUUM is online — it does not block concurrent reads or writes.
/// </summary>
[Service]
public class HoldingsTableMaintenanceService
{
    // VACUUM only processes pages changed since the last run, so a post-sweep pass is cheap — but
    // cap it generously so a one-off large pass can never hang the ingestion worker indefinitely.
    private static readonly TimeSpan VacuumTimeout = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HoldingsTableMaintenanceService> _logger;

    public HoldingsTableMaintenanceService(
        IServiceScopeFactory scopeFactory,
        ILogger<HoldingsTableMaintenanceService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public virtual async Task VacuumInstitutionalHoldings(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var connection = dbContext.Database.GetDbConnection();

        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // VACUUM forbids bind parameters and a surrounding transaction; the table name is a constant.
        command.CommandText = "VACUUM (ANALYZE) \"InstitutionalHolding\"";
        command.CommandTimeout = (int)VacuumTimeout.TotalSeconds;

        _logger.LogInformation("Vacuuming InstitutionalHolding after 13F ingestion");
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("InstitutionalHolding vacuum complete");
    }
}
