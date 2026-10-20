using Equibles.Holdings.BusinessLogic.Links;
using Equibles.Holdings.BusinessLogic.Ownership;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Equibles.Holdings.BusinessLogic.Extensions;

public static class StockOwnershipServiceCollectionExtensions
{
    /// <summary>
    /// Registers the unified holder list: its reader and the SEC lanes (13F, 13D and 13G, insiders,
    /// registered funds). Each lane is added once however often this runs, so a host that wires it
    /// twice never counts a row twice; a deployment adds further lanes with <c>TryAddEnumerable</c>.
    /// </summary>
    public static IServiceCollection AddStockOwnership(this IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IHolderSourceContributor, Form13FContributor>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IHolderSourceContributor, Schedule13DGContributor>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IHolderSourceContributor, InsiderContributor>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IHolderSourceContributor, FundContributor>()
        );
        // Hosts that auto-wire these assemblies register the same services; TryAdd keeps theirs.
        services.TryAddScoped<ISharesOutstandingProvider, SharesOutstandingProvider>();
        services.TryAddScoped<IHolderRollupReader, HolderRollupReader>();
        services.TryAddScoped<StockOwnershipReader>();
        services.TryAddScoped<StockBeneficialOwnershipReader>();
        services.AddMemoryCache();
        return services;
    }
}
