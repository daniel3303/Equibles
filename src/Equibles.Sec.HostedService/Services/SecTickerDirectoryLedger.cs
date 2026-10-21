using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Records each capture of SEC's ticker directory as dated <see cref="SecTickerDirectoryObservation"/>
/// spans: a triple the previous capture also listed extends its span to this capture's date, and any
/// other triple opens a new span. Absence is read only from a capture that listed other rows, so a
/// span can close but is never rewritten.
/// </summary>
public static class SecTickerDirectoryLedger
{
    public static async Task<int> Record(
        DbContext dbContext,
        IEnumerable<CompanyInfo> companies,
        DateOnly observedOn,
        CancellationToken cancellationToken = default
    )
    {
        var incoming = new HashSet<(string Cik, string Ticker, string Exchange)>();
        foreach (var company in companies)
        {
            var cik = CikNormalizer.Canonicalize(company.Cik);
            if (cik == null)
                continue;
            foreach (var (listedTicker, exchange) in company.TickerExchanges)
            {
                var ticker = TickerNormalizer.NormalizeIdentity(listedTicker);
                if (
                    ticker == null
                    || string.IsNullOrWhiteSpace(exchange)
                    || exchange.Length > SecTickerDirectoryObservation.ExchangeMaxLength
                )
                    continue;
                incoming.Add((cik, ticker, exchange.Trim()));
            }
        }
        // An empty capture is a failed fetch, not a directory that lists nobody.
        if (incoming.Count == 0)
            return 0;

        var observations = dbContext.Set<SecTickerDirectoryObservation>();
        var latest = await observations.MaxAsync(
            observation => (DateOnly?)observation.LastObservedOn,
            cancellationToken
        );
        // A clock that went backwards would date a new span before the ones it follows.
        if (latest > observedOn)
            return 0;

        var open =
            latest == null
                ? []
                : await observations
                    .Where(observation => observation.LastObservedOn == latest)
                    .ToListAsync(cancellationToken);
        var openByKey = new Dictionary<(string, string, string), SecTickerDirectoryObservation>();
        foreach (var span in open)
            openByKey.TryAdd((span.Cik, span.Ticker, span.Exchange), span);

        var written = 0;
        foreach (var key in incoming)
        {
            if (openByKey.TryGetValue(key, out var span))
            {
                if (span.LastObservedOn == observedOn)
                    continue;
                span.LastObservedOn = observedOn;
            }
            else
            {
                observations.Add(
                    new SecTickerDirectoryObservation
                    {
                        Cik = key.Cik,
                        Ticker = key.Ticker,
                        Exchange = key.Exchange,
                        FirstObservedOn = observedOn,
                        LastObservedOn = observedOn,
                    }
                );
            }
            written++;
        }

        if (written > 0)
            await dbContext.SaveChangesAsync(cancellationToken);
        return written;
    }
}
