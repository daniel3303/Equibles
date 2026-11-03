using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Equibles.CorporateActions.BusinessLogic;

/// <summary>
/// Captures that predate listing attribution stored issuer-only dividends, which listing-keyed
/// reads never serve. Re-reading such an issuer's history from the source through the listing
/// capture path writes attributed rows; the issuer-only rows stay as captured evidence.
/// </summary>
[Service]
public class UnattributedDividendRecaptureManager
{
    // A provider can date one payment a day or two apart from another; such a row already has
    // an attributed twin and needs no re-read.
    internal const int MatchToleranceDays = 3;

    private readonly CashDividendRepository _dividendRepository;
    private readonly EquityIssuerRepository _issuerRepository;
    private readonly CashDividendBackfillManager _backfillManager;
    private readonly ILogger<UnattributedDividendRecaptureManager> _logger;

    public UnattributedDividendRecaptureManager(
        CashDividendRepository dividendRepository,
        EquityIssuerRepository issuerRepository,
        CashDividendBackfillManager backfillManager,
        ILogger<UnattributedDividendRecaptureManager> logger
    )
    {
        _dividendRepository = dividendRepository;
        _issuerRepository = issuerRepository;
        _backfillManager = backfillManager;
        _logger = logger;
    }

    /// <summary>
    /// Re-reads up to <paramref name="batchSize"/> issuers not yet attempted whose current
    /// presentation listing is an active US line, and returns the issuers attempted; an empty
    /// result means the pass is done. An issuer the source no longer serves stays issuer-only.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> RecaptureNext(
        IReadOnlyCollection<Guid> attempted,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        var attemptedIds = attempted.ToList();
        var batch = await _dividendRepository
            .GetUnattributedWithoutListingRow(MatchToleranceDays)
            .Where(row =>
                row.Issuer.Presentation.Listing.Active
                && row.Issuer.Presentation.Listing.MarketCountryCode == "US"
                && !attemptedIds.Contains(row.EquityIssuerId)
            )
            .GroupBy(row => row.EquityIssuerId)
            .Select(group => new
            {
                IssuerId = group.Key,
                OldestExDate = group.Min(row => row.ExDate),
            })
            .OrderBy(row => row.IssuerId)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var captured = 0;
        foreach (var row in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var issuer = await _issuerRepository.Get(row.IssuerId);
            if (issuer?.Presentation?.Listing == null)
                continue;
            try
            {
                captured += await _backfillManager.BackfillHistory(
                    issuer,
                    row.OldestExDate.AddDays(-MatchToleranceDays),
                    cancellationToken
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            // A client timeout is an OperationCanceledException too; it is one issuer's failure.
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Dividend re-capture for {Ticker} ({IssuerId}) failed; leaving its issuer-only rows",
                    issuer.Presentation.Listing.Ticker,
                    issuer.Id
                );
            }
            finally
            {
                // A failed save leaves its rows tracked on the shared context, and the next
                // issuer's save would send them again.
                _dividendRepository.ClearChangeTracker();
            }
        }

        if (batch.Count > 0)
            _logger.LogInformation(
                "Dividend re-capture: {Issuers} issuers re-read, {Captured} attributed dividends written",
                batch.Count,
                captured
            );
        return batch.Select(row => row.IssuerId).ToList();
    }
}
