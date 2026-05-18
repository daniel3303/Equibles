using System.Threading;
using System.Threading.Tasks;
using Equibles.Search;
using Equibles.Search.Abstractions;
using Equibles.CommonStocks.Repositories.Search;
using Equibles.Search.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Search;

public class SearchServiceCollectionExtensionsTests
{
    [Fact]
    public void AddEquiblesSearch_DiscoversProviderInEquiblesAssembly_AndRegistersAggregator()
    {
        var services = new ServiceCollection();

        services.AddEquiblesSearch();

        // The test assembly is "Equibles.UnitTests" (Equibles.* prefix), so the scanner must
        // discover this provider by interface with no explicit registration — the core
        // extensibility guarantee: a new module joins search just by being loaded.
        services
            .Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(ISearchProvider)
                && descriptor.ImplementationType == typeof(DiscoverableTestProvider)
            );
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(SearchAggregator));
    }

    [Fact]
    public void AddEquiblesSearch_WithoutAddAllRepositories_StillDiscoversRealModuleProvider()
    {
        // Ordering-independence (issue #887): AddEquiblesSearch self-loads plugin assemblies via
        // PluginLoader, so a real module provider is discovered even though AddAllRepositories
        // (the registrar it used to implicitly depend on) was never called.
        var services = new ServiceCollection();

        services.AddEquiblesSearch();

        services
            .Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(ISearchProvider)
                && descriptor.ImplementationType == typeof(CommonStockSearchProvider)
            );
    }

    [Fact]
    public void AddEquiblesSearch_CalledTwice_DoesNotDoubleRegisterProvider()
    {
        var services = new ServiceCollection();

        services.AddEquiblesSearch();
        services.AddEquiblesSearch();

        services
            .Count(descriptor =>
                descriptor.ServiceType == typeof(ISearchProvider)
                && descriptor.ImplementationType == typeof(DiscoverableTestProvider)
            )
            .Should()
            .Be(1);
    }

    // Public so assembly scanning finds it exactly as it would a real module provider.
    public class DiscoverableTestProvider : ISearchProvider
    {
        public string Category => "TestCategory";

        public int Order => 999;

        public Task<SearchResultGroup> Search(
            SearchRequest request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new SearchResultGroup { Category = Category, Order = Order });
        }
    }
}
