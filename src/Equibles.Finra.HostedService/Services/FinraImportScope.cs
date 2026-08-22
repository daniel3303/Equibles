using System.Security.Cryptography;
using System.Text;

namespace Equibles.Finra.HostedService.Services;

public static class FinraImportScope
{
    public static string Resolve(IReadOnlyCollection<string> tickers)
    {
        if (tickers == null || tickers.Count == 0)
            return "all";

        var normalized = tickers
            .Select(ticker => ticker?.Trim().ToUpperInvariant())
            .Where(ticker => !string.IsNullOrEmpty(ticker))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ticker => ticker, StringComparer.Ordinal)
            .ToList();
        if (normalized.Count == 0)
            return "all";

        var payload = string.Join('\n', normalized);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"tickers:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public static string ResolveStockUniverse(IReadOnlyDictionary<string, Guid> stocks)
    {
        ArgumentNullException.ThrowIfNull(stocks);

        var payload = string.Join(
            '\n',
            stocks
                .OrderBy(stock => stock.Key, StringComparer.Ordinal)
                .Select(stock => $"{stock.Key}\0{stock.Value:N}")
        );
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"stocks:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
