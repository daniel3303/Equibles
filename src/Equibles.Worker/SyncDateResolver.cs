using Equibles.Core.Configuration;

namespace Equibles.Worker;

public static class SyncDateResolver {
    private static readonly DateOnly DefaultMinDate = new(2020, 1, 1);

    /// <summary>
    /// Determines the start date for a sync operation based on the latest date already in the database.
    /// If no data exists, falls back to WorkerOptions.MinSyncDate or 2020-01-01.
    /// </summary>
    public static DateOnly Resolve(DateOnly latestDateInDb, WorkerOptions workerOptions) {
        if (latestDateInDb != default) {
            return latestDateInDb.AddDays(1);
        }

        return workerOptions.MinSyncDate.HasValue
            ? DateOnly.FromDateTime(workerOptions.MinSyncDate.Value)
            : DefaultMinDate;
    }
}
