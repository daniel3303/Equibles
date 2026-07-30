using Equibles.Sec.Repositories;
using Npgsql;

namespace Equibles.UnitTests.Sec;

// Pins ChunkRepository's statement-timeout classification: the backend 57014 is definitive,
// a bare TimeoutException only counts when the run actually elapsed about the statement
// budget (a pool-exhaustion or connect timeout carries the same TimeoutException but waits
// on the 15s connection-string timeout — relabelling THAT as the statement budget would
// send callers into a doomed degrade pass), and other Postgres faults never classify.
public class ChunkRepositoryStatementTimeoutClassificationTests
{
    private const int BudgetSeconds = 5;

    [Fact]
    public void QueryCanceled_Classifies_RegardlessOfElapsed()
    {
        var canceled = NewPostgresException(PostgresErrorCodes.QueryCanceled);

        Assert.True(
            ChunkRepository.IsStatementTimeout(canceled, TimeSpan.FromSeconds(30), BudgetSeconds)
        );
    }

    [Fact]
    public void QueryCanceled_WrappedInAProviderException_Classifies()
    {
        var wrapped = new InvalidOperationException(
            "An exception occurred while reading",
            NewPostgresException(PostgresErrorCodes.QueryCanceled)
        );

        Assert.True(
            ChunkRepository.IsStatementTimeout(wrapped, TimeSpan.FromSeconds(5), BudgetSeconds)
        );
    }

    [Fact]
    public void TimeoutException_AtTheBudget_Classifies()
    {
        // Npgsql's own CommandTimeout shape: NpgsqlException wrapping a TimeoutException,
        // thrown right around the statement budget.
        var commandTimeout = new NpgsqlException(
            "Exception while reading from stream",
            new TimeoutException("Timeout during reading attempt")
        );

        Assert.True(
            ChunkRepository.IsStatementTimeout(
                commandTimeout,
                TimeSpan.FromSeconds(BudgetSeconds + 1),
                BudgetSeconds
            )
        );
    }

    [Fact]
    public void TimeoutException_LongAfterTheBudget_DoesNotClassify()
    {
        // The pool-exhaustion / connect-timeout shape: same TimeoutException inside, but
        // it elapses on the 15s connection-string timeout, far past the statement budget.
        var poolExhausted = new NpgsqlException(
            "The connection pool has been exhausted",
            new TimeoutException()
        );

        Assert.False(
            ChunkRepository.IsStatementTimeout(
                poolExhausted,
                TimeSpan.FromSeconds(15),
                BudgetSeconds
            )
        );
    }

    [Fact]
    public void OtherPostgresFault_DoesNotClassify()
    {
        var undefinedTable = NewPostgresException(PostgresErrorCodes.UndefinedTable);

        Assert.False(
            ChunkRepository.IsStatementTimeout(
                undefinedTable,
                TimeSpan.FromSeconds(1),
                BudgetSeconds
            )
        );
    }

    private static PostgresException NewPostgresException(string sqlState) =>
        new("message", "ERROR", "ERROR", sqlState);
}
