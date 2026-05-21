using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Exceptions;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;

namespace Equibles.CommonStocks.BusinessLogic;

[Service]
public class CommonStockManager
{
    private readonly CommonStockRepository _commonStockRepository;
    private readonly IPublishEndpoint _publishEndpoint;

    public CommonStockManager(
        CommonStockRepository commonStockRepository,
        IPublishEndpoint publishEndpoint
    )
    {
        _commonStockRepository = commonStockRepository;
        _publishEndpoint = publishEndpoint;
    }

    /// <summary>
    /// Sets a stock's CUSIP. When the value actually changes, publishes
    /// <see cref="StockCusipChanged"/> (before SaveChanges, so the EF outbox
    /// captures it in the same transaction) so the Holdings module can
    /// backfill quarterly 13F data sets that were processed while this stock
    /// was still unresolvable. A no-op change publishes nothing.
    /// </summary>
    public async Task SetCusip(CommonStock commonStock, string cusip)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        if (string.Equals(commonStock.Cusip, cusip, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var previousCusip = commonStock.Cusip;
        commonStock.Cusip = cusip;

        // Publish before SaveChanges (outbox pattern).
        await _publishEndpoint.Publish(
            new StockCusipChanged(commonStock.Id, commonStock.Ticker, previousCusip, cusip)
        );
        await _commonStockRepository.SaveChanges();
    }

    /// <summary>
    /// Sets the company's fiscal year-end (month 1-12, optional day 1-31),
    /// sourced from SEC EDGAR's submissions <c>fiscalYearEnd</c> field. A
    /// no-op change persists nothing. Saves directly via the repository — like
    /// <see cref="SetCusip"/>, this mutates a single non-key field and must not
    /// re-run the full ticker/CIK uniqueness validation.
    /// </summary>
    public async Task SetFiscalYearEnd(CommonStock commonStock, int month, int? day)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        if (month is < 1 or > 12)
        {
            throw new DomainValidationException(
                $"Fiscal year-end month must be between 1 and 12, got {month}"
            );
        }

        if (day is < 1 or > 31)
        {
            throw new DomainValidationException(
                $"Fiscal year-end day must be between 1 and 31, got {day}"
            );
        }

        if (commonStock.FiscalYearEndMonth == month && commonStock.FiscalYearEndDay == day)
        {
            return;
        }

        commonStock.FiscalYearEndMonth = month;
        commonStock.FiscalYearEndDay = day;
        await _commonStockRepository.SaveChanges();
    }

    public async Task<CommonStock> Create(CommonStock commonStock)
    {
        await ValidateCommonStock(commonStock, true);
        _commonStockRepository.Add(commonStock);
        await _commonStockRepository.SaveChanges();
        return commonStock;
    }

    public async Task<CommonStock> Update(CommonStock commonStock)
    {
        await ValidateCommonStock(commonStock, false);
        await _commonStockRepository.SaveChanges();
        return commonStock;
    }

    private async Task ValidateCommonStock(CommonStock commonStock, bool isInsert)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        // Required fields: a whitespace-only value is not a provided value.
        // Ticker is the globally-unique key and the lookup key, so accepting
        // whitespace would corrupt the uniqueness invariant and ticker lookups.
        RequireNonBlank(commonStock.Ticker, "Ticker");
        RequireNonBlank(commonStock.Name, "Name");
        RequireNonBlank(commonStock.Cik, "Cik");

        if (commonStock.MarketCapitalization < 0)
        {
            throw new DomainValidationException("MarketCapitalization cannot be negative");
        }

        if (commonStock.SharesOutStanding < 0)
        {
            throw new DomainValidationException("SharesOutStanding cannot be negative");
        }

        // Primary ticker must be globally unique across all companies.
        var existingByTicker = await _commonStockRepository.GetByPrimaryTicker(commonStock.Ticker);
        if (existingByTicker != null && (isInsert || existingByTicker.Id != commonStock.Id))
        {
            throw new DomainValidationException(
                $"CommonStock with ticker {commonStock.Ticker} already exists"
            );
        }

        var existingByCik = await _commonStockRepository.GetByCik(commonStock.Cik);
        if (existingByCik != null && (isInsert || existingByCik.Id != commonStock.Id))
        {
            throw new DomainValidationException(
                $"CommonStock with cik {commonStock.Cik} already exists"
            );
        }

        // Secondary tickers are allowed to overlap with primary or secondary tickers of other
        // companies. In SEC filings a preferred-share ticker can legitimately appear under both
        // the parent REIT filer and its operating-partnership filer, so cross-company overlap
        // is valid. Lookups resolve ambiguity via GetByTicker's primary-first ordering.
    }

    private static void RequireNonBlank(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"{name} is required");
        }
    }
}
