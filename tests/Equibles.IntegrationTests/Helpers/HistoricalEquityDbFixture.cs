using Xunit;

namespace Equibles.IntegrationTests.Helpers;

// Historical bridge contracts run on the last schema that contained those bridges.
// Normal runtime tests use ParadeDbFixture and the fully retired production schema.
public sealed class HistoricalEquityDbFixture : ParadeDbFixture
{
    protected override string MigrationTarget =>
        "20260913025100_PreserveHoldingObservationIdentity";
    protected override bool IncludeLegacyMappings => true;
}

[CollectionDefinition(Name)]
public sealed class HistoricalEquityDbCollection : ICollectionFixture<HistoricalEquityDbFixture>
{
    public const string Name = "Historical equity migrations";
}
