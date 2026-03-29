namespace Equibles.Worker;

public static class BatchPersister {
    public static async Task<int> Persist<TEntity>(
        IEnumerable<TEntity> items,
        int batchSize,
        Func<List<TEntity>, Task> flushBatch
    ) {
        var batch = new List<TEntity>(batchSize);
        var totalInserted = 0;

        foreach (var item in items) {
            batch.Add(item);

            if (batch.Count >= batchSize) {
                await flushBatch(batch);
                totalInserted += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0) {
            await flushBatch(batch);
            totalInserted += batch.Count;
        }

        return totalInserted;
    }
}
