using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.TestSupport;
using Xunit;

namespace Equibles.IntegrationTests.CommonStocks;

public class FinancialEquityStorageRetirementTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture = new();

    public async Task InitializeAsync() => await _fixture.InitializeAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Theory]
    [InlineData("preserve")]
    [InlineData("altered-native-index")]
    [InlineData("unknown-owner-statistics")]
    [InlineData("preserve-alias")]
    [InlineData("wrong-sibling-observation")]
    [InlineData("wrong-price-mapping-owner")]
    [InlineData("unknown-owner-constraint")]
    [InlineData("missing-native-owner-key")]
    [InlineData("unmapped-original-issuer")]
    [InlineData("price-mismatch")]
    [InlineData("unmapped-price-column")]
    [InlineData("unvalidated-owner")]
    [InlineData("unarchived-source")]
    [InlineData("unknown-dependent-view")]
    public async Task FinalContract_PreservesNativeDataAndRefusesIncompleteReconciliation(
        string scenario
    )
    {
        await using var db = _fixture.CreateDbContext();
        await using var native = _fixture.CreateNativeDbContext();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Equibles.sln")))
            root = root.Parent;
        var sql = await File.ReadAllTextAsync(
            Path.Combine(
                root!.FullName,
                "src/Equibles.Migrations/Infrastructure/RetireLegacyFinancialEquityStorage20260913.sql"
            )
        );
        await FinancialEquityRetirementProbe.Run(db, native, sql, scenario);
    }
}
