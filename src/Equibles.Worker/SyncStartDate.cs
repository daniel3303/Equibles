using Equibles.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Worker;

public static class SyncStartDate
{
    /// <summary>
    /// Resolves the next sync start date by reading the latest stored date from a repository
    /// in a fresh DI scope and feeding it through <see cref="SyncDateResolver.Resolve"/>.
    /// </summary>
    public static async Task<DateOnly> Resolve<TRepository>(
        IServiceScopeFactory scopeFactory,
        WorkerOptions workerOptions,
        Func<TRepository, IQueryable<DateOnly>> latestDateQuery,
        CancellationToken cancellationToken
    )
        where TRepository : notnull
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<TRepository>();
        var latestDate = await latestDateQuery(repo).FirstOrDefaultAsync(cancellationToken);
        return SyncDateResolver.Resolve(latestDate, workerOptions);
    }
}
