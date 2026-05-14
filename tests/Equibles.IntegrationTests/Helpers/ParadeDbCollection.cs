using Xunit;

namespace Equibles.IntegrationTests.Helpers;

/// <summary>
/// xUnit collection that shares a single <see cref="ParadeDbFixture"/> across every MCP
/// integration test class. Tests in this collection run sequentially — the cost of one
/// shared container outweighs the parallelism we'd buy by having one per class.
/// </summary>
[CollectionDefinition(Name)]
public class ParadeDbCollection : ICollectionFixture<ParadeDbFixture>
{
    public const string Name = "ParadeDb";
}
