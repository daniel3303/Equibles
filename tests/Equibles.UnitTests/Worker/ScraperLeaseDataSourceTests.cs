#nullable enable

using Equibles.Worker;

namespace Equibles.UnitTests.Worker;

public class ScraperLeaseDataSourceTests
{
    private const string ConnectionString =
        "Host=localhost;Database=equibles;Username=postgres;Password=postgres";

    [Fact]
    public async Task ReserveConnection_ConfiguredCapacity_RejectsAnotherLeaseImmediately()
    {
        await using var dataSource = new ScraperLeaseDataSource(ConnectionString, 2);
        dataSource.ReserveConnection();
        dataSource.ReserveConnection();

        try
        {
            var act = dataSource.ReserveConnection;

            act.Should().Throw<ScraperLeasePoolUnavailableException>();
        }
        finally
        {
            dataSource.ReleaseConnection();
            dataSource.ReleaseConnection();
        }
    }

    [Fact]
    public void Constructor_NonPositiveCapacity_ThrowsConfigurationError()
    {
        var act = () => new ScraperLeaseDataSource(ConnectionString, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
