using System.Data.Common;
using Npgsql;

namespace Equibles.Worker;

internal sealed class ScraperLeaseDataSource : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SemaphoreSlim _availableConnections;
    private int _disposed;

    internal ScraperLeaseDataSource(string connectionString, int maximumPoolSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPoolSize, 1);

        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = maximumPoolSize,
            // A multiplexed logical connection does not own one stable PostgreSQL session, which
            // would invalidate a session-scoped advisory lock held across the scraper cycle.
            Multiplexing = false,
            // Npgsql's normal DISCARD ALL reset releases advisory locks if explicit cleanup fails.
            NoResetOnClose = false,
        };

        // Lease sessions live for a whole scraper cycle. Keeping them in a small, separate data
        // source prevents lane count from consuming the query pool, while the matching gate turns
        // excess startup demand into an immediate skip instead of an Npgsql pool timeout.
        _dataSource = NpgsqlDataSource.Create(connectionStringBuilder.ConnectionString);
        _availableConnections = new SemaphoreSlim(maximumPoolSize, maximumPoolSize);
        MaximumPoolSize = maximumPoolSize;
    }

    internal string ConnectionString => _dataSource.ConnectionString;
    internal int MaximumPoolSize { get; }

    internal void ReserveConnection()
    {
        if (!_availableConnections.Wait(0))
            throw new ScraperLeasePoolUnavailableException();
    }

    internal void ReleaseConnection() => _availableConnections.Release();

    internal async ValueTask<DbConnection> OpenConnection(CancellationToken cancellationToken) =>
        await _dataSource.OpenConnectionAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _dataSource.DisposeAsync();
        _availableConnections.Dispose();
    }
}
