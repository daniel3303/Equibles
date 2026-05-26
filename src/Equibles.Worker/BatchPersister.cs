using Equibles.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Worker;

public static class BatchPersister
{
    public static async Task<int> Persist<TEntity>(
        IEnumerable<TEntity> items,
        int batchSize,
        Func<List<TEntity>, Task> flushBatch
    )
    {
        var batch = new List<TEntity>(batchSize);
        var totalInserted = 0;

        foreach (var item in items)
        {
            batch.Add(item);

            if (batch.Count >= batchSize)
            {
                await flushBatch(batch);
                totalInserted += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await flushBatch(batch);
            totalInserted += batch.Count;
        }

        return totalInserted;
    }

    public static Task<int> Persist<TEntity, TRepository>(
        IEnumerable<TEntity> items,
        int batchSize,
        IServiceScopeFactory scopeFactory
    )
        where TEntity : class
        where TRepository : BaseRepository<TEntity>
    {
        return Persist(
            items,
            batchSize,
            async batch =>
            {
                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<TRepository>();
                repo.AddRange(batch);
                await repo.SaveChanges();
            }
        );
    }
}
