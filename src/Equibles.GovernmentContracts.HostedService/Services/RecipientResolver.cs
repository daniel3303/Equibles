using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.GovernmentContracts.HostedService.Services;

/// <summary>
/// Resolves a USAspending recipient name to a public company in our <c>CommonStock</c>
/// universe via an exact normalised-name match. Builds the lookup once per import cycle.
/// Names that normalise to the same key for more than one distinct stock are treated as
/// ambiguous and dropped, so a wrong link is never asserted.
/// </summary>
[Service]
public class RecipientResolver
{
    private readonly IServiceScopeFactory _scopeFactory;

    public RecipientResolver(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<IReadOnlyDictionary<string, Guid>> BuildLookup(
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var stocks = await repository
            .GetAll()
            .Where(s => s.Name != null)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(cancellationToken);

        var lookup = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);

        foreach (var stock in stocks)
        {
            var key = RecipientNameNormalizer.Normalize(stock.Name);
            if (key == null)
                continue;

            if (lookup.TryGetValue(key, out var existingId))
            {
                if (existingId != stock.Id)
                    ambiguous.Add(key);
            }
            else
            {
                lookup[key] = stock.Id;
            }
        }

        foreach (var key in ambiguous)
            lookup.Remove(key);

        return lookup;
    }

    public static Guid? Resolve(string recipientName, IReadOnlyDictionary<string, Guid> lookup)
    {
        var key = RecipientNameNormalizer.Normalize(recipientName);
        if (key == null)
            return null;

        return lookup.TryGetValue(key, out var id) ? id : null;
    }
}
