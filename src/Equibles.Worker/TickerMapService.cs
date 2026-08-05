using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Worker;

[Service]
public class TickerMapService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TickerMapService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Maps stored primary tickers to stock ids. The default comparer is case-insensitive for
    /// sources that vary letter case of the SAME security's symbol. Sources whose casing is
    /// itself identity — FINRA writes preferred/when-issued suffixes in lowercase (TpC is a
    /// DIFFERENT security from TPC) — must pass <see cref="StringComparer.Ordinal"/>, or the
    /// case-fold silently merges two securities onto one stock.
    /// </summary>
    public async Task<Dictionary<string, Guid>> Build(
        List<string> tickersToSync,
        CancellationToken cancellationToken,
        StringComparer comparer = null
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var query =
            tickersToSync?.Count > 0 ? stockRepo.GetByTickers(tickersToSync) : stockRepo.GetAll();

        return await query.ToDictionaryAsync(
            s => s.Ticker,
            s => s.Id,
            comparer ?? StringComparer.OrdinalIgnoreCase,
            cancellationToken
        );
    }
}
