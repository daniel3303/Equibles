using Equibles.Search.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Search;

/// <summary>
/// Fans a query out to every registered <see cref="ISearchProvider"/> and returns the non-empty
/// groups ordered for display. Each provider runs in its own DI scope so providers execute in
/// parallel safely — the request-scoped <c>EquiblesDbContext</c> is not concurrency-safe, so they
/// cannot share one. A provider that throws or exceeds <see cref="ProviderTimeout"/> is logged and
/// dropped; one slow or broken module never breaks the results page.
/// </summary>
public class SearchAggregator
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SearchAggregator> _logger;

    public SearchAggregator(IServiceScopeFactory scopeFactory, ILogger<SearchAggregator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<List<SearchResultGroup>> Search(
        string query,
        int maxPerProvider,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // Discover the concrete provider types once, then resolve each by type identity inside its
        // own scope. Mapping by Type — not by positional index across two separate GetServices
        // enumerations — stays correct under decorators, conditional or reordered registration.
        List<Type> providerTypes;
        using (var probeScope = _scopeFactory.CreateScope())
        {
            providerTypes = probeScope
                .ServiceProvider.GetServices<ISearchProvider>()
                .Select(provider => provider.GetType())
                .Distinct()
                .ToList();
        }

        var request = new SearchRequest { Query = query.Trim(), MaxPerProvider = maxPerProvider };

        var groups = await Task.WhenAll(
            providerTypes.Select(providerType =>
                RunProvider(providerType, request, cancellationToken)
            )
        );

        return groups
            .Where(group => group.Hits.Count > 0)
            .OrderBy(group => group.Order)
            .ThenBy(group => group.Category ?? string.Empty, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<SearchResultGroup> RunProvider(
        Type providerType,
        SearchRequest request,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var provider = scope
            .ServiceProvider.GetServices<ISearchProvider>()
            .First(candidate => candidate.GetType() == providerType);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutSource.CancelAfter(ProviderTimeout);

        try
        {
            var searchTask = provider.Search(request, timeoutSource.Token);
            // Backstop: a provider that ignores the token still cannot stall the page.
            var completed = await Task.WhenAny(
                searchTask,
                Task.Delay(ProviderTimeout, timeoutSource.Token)
            );

            if (completed != searchTask)
            {
                _logger.LogWarning(
                    "Search provider {Provider} timed out after {Timeout}s; group omitted",
                    providerType.Name,
                    ProviderTimeout.TotalSeconds
                );
                return Empty;
            }

            return await searchTask ?? Empty;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Search provider {Provider} failed for query {Query}; group omitted",
                providerType.Name,
                request.Query
            );
            return Empty;
        }
    }

    // Empty (no-hit) sentinel — RunProvider never returns null, so the ordering step downstream
    // cannot NRE and the filter in Search drops degraded providers uniformly.
    private static SearchResultGroup Empty => new();
}
