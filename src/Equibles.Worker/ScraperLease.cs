using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Worker;

internal sealed class ScraperLease : IAsyncDisposable
{
    private const string AcquireSql = "SELECT pg_try_advisory_lock(@lockKey)";
    private const string ReleaseSql = "SELECT pg_advisory_unlock(@lockKey)";
    private const int ReleaseCommandTimeoutSeconds = 5;
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private readonly AsyncServiceScope _scope;
    private readonly ScraperLeaseDataSource _dataSource;
    private readonly DbConnection _connection;
    private readonly long _lockKey;
    private readonly string _workerName;
    private readonly ILogger _logger;
    private int _disposed;

    private ScraperLease(
        AsyncServiceScope scope,
        ScraperLeaseDataSource dataSource,
        DbConnection connection,
        long lockKey,
        string workerName,
        ILogger logger
    )
    {
        _scope = scope;
        _dataSource = dataSource;
        _connection = connection;
        _lockKey = lockKey;
        _workerName = workerName;
        _logger = logger;
    }

    /// <param name="laneId">
    /// Stable identity of the lane. Two instances serialize only if this matches exactly, so it
    /// must never track a display string — see <c>BaseScraperWorker.LaneId</c>.
    /// </param>
    /// <param name="workerName">Display name, used only for logging.</param>
    internal static async Task<ScraperLease> TryAcquire(
        IServiceScopeFactory scopeFactory,
        string laneId,
        string workerName,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var lockKey = ComputeLockKey(laneId);
        var scope = scopeFactory.CreateAsyncScope();
        ScraperLeaseDataSource dataSource = null;
        DbConnection connection = null;
        var reservationHeld = false;
        var retainResources = false;

        try
        {
            dataSource = scope.ServiceProvider.GetRequiredService<ScraperLeaseDataSource>();
            dataSource.ReserveConnection();
            reservationHeld = true;
            connection = await dataSource.OpenConnection(cancellationToken);

            await using var command = CreateCommand(connection, AcquireSql, lockKey);
            if (await command.ExecuteScalarAsync(cancellationToken) is not true)
                return null;

            // PostgreSQL advisory locks belong to a database session. This exact connection remains
            // open until DisposeAsync releases the lock after the whole scraper cycle completes.
            retainResources = true;
            return new ScraperLease(scope, dataSource, connection, lockKey, workerName, logger);
        }
        finally
        {
            if (!retainResources)
            {
                await ReleaseAcquisitionResources(
                    scope,
                    dataSource,
                    connection,
                    reservationHeld,
                    workerName,
                    logger
                );
            }
        }
    }

    internal static long ComputeLockKey(string workerName)
    {
        ArgumentNullException.ThrowIfNull(workerName);

        var hash = FnvOffsetBasis;
        foreach (var value in Encoding.UTF8.GetBytes(workerName))
        {
            hash ^= value;
            hash *= FnvPrime;
        }

        return unchecked((long)hash);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (_connection.State == ConnectionState.Open)
            {
                await using var command = CreateCommand(_connection, ReleaseSql, _lockKey);
                command.CommandTimeout = ReleaseCommandTimeoutSeconds;
                if (await command.ExecuteScalarAsync(CancellationToken.None) is not true)
                {
                    _logger.LogWarning(
                        "PostgreSQL reported that the scraper lane lease for {Worker} was not held",
                        _workerName
                    );
                }
            }
        }
        catch (Exception ex)
        {
            // Cleanup must not replace a DoWork exception. Disposing the connection below resets a
            // healthy pooled session or drops a broken physical session after unlock cannot run.
            _logger.LogWarning(
                ex,
                "Failed to explicitly release scraper lane lease for {Worker}",
                _workerName
            );
        }
        finally
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to close scraper lane lease connection for {Worker}",
                    _workerName
                );
            }

            try
            {
                _dataSource.ReleaseConnection();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to release scraper lane lease pool capacity for {Worker}",
                    _workerName
                );
            }

            try
            {
                await _scope.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to dispose scraper lane lease scope for {Worker}",
                    _workerName
                );
            }
        }
    }

    private static async ValueTask ReleaseAcquisitionResources(
        AsyncServiceScope scope,
        ScraperLeaseDataSource dataSource,
        DbConnection connection,
        bool reservationHeld,
        string workerName,
        ILogger logger
    )
    {
        if (connection is not null)
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to close unacquired scraper lane lease connection for {Worker}",
                    workerName
                );
            }
        }

        if (reservationHeld)
        {
            try
            {
                dataSource.ReleaseConnection();
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to release unacquired scraper lane lease pool capacity for {Worker}",
                    workerName
                );
            }
        }

        try
        {
            await scope.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to dispose unacquired scraper lane lease scope for {Worker}",
                workerName
            );
        }
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        string commandText,
        long lockKey
    )
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "lockKey";
        parameter.DbType = DbType.Int64;
        parameter.Value = lockKey;
        command.Parameters.Add(parameter);

        return command;
    }
}
