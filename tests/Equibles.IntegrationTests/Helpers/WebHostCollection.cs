using Xunit;

namespace Equibles.IntegrationTests.Helpers;

/// <summary>
/// xUnit collection sharing one <see cref="WebHostFixture"/> (and its ParadeDB
/// container) across every in-process Web view-rendering test class.
/// </summary>
[CollectionDefinition(Name)]
public class WebHostCollection : ICollectionFixture<WebHostFixture>
{
    public const string Name = "WebHost";
}
