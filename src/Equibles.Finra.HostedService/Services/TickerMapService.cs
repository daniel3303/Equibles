using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Microsoft.EntityFrameworkCore;

namespace Equibles.ShortData.HostedService.Services;

[Service]
public class TickerMapService {
    private readonly IServiceScopeFactory _scopeFactory;

    public TickerMapService(IServiceScopeFactory scopeFactory) {
        _scopeFactory = scopeFactory;
    }

    public async Task<Dictionary<string, Guid>> Build(
        List<string> tickersToSync,
        CancellationToken cancellationToken
    ) {
        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var query = tickersToSync?.Count > 0
            ? stockRepo.GetByTickers(tickersToSync)
            : stockRepo.GetAll();

        return await query
            .ToDictionaryAsync(s => s.Ticker, s => s.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
    }
}
