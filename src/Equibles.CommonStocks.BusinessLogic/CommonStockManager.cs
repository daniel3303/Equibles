using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Exceptions;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.BusinessLogic;

[Service]
public class CommonStockManager
{
    private readonly CommonStockRepository _commonStockRepository;
    private readonly IBus _bus;

    public CommonStockManager(CommonStockRepository commonStockRepository, IBus bus)
    {
        _commonStockRepository = commonStockRepository;
        _bus = bus;
    }

    /// <summary>
    /// Sets a stock's CUSIP. When the value actually changes, publishes
    /// <see cref="StockCusipChanged"/> after SaveChanges so the Holdings module can
    /// backfill quarterly 13F data sets that were processed while this stock
    /// was still unresolvable. A no-op change publishes nothing.
    /// <para>
    /// Replacing a non-null CUSIP (an issuer-level CUSIP change) also records the
    /// retired value as a <see cref="CommonStockCusipAlias"/>. Filings keep
    /// referencing the old CUSIP — laggard 13F filers for a quarter or two, and
    /// historical data sets forever — so import-time resolution must keep mapping
    /// it to this stock. Without the alias, the backfill triggered by the change
    /// would silently drop old-CUSIP lines wherever a restatement amendment
    /// deletes and re-inserts a quarter.
    /// </para>
    /// <para>
    /// This is a financial-domain event, so it publishes via the root
    /// <see cref="IBus"/> rather than the scoped <c>IPublishEndpoint</c>. A host
    /// that enables a bus outbox on a different context (e.g. the commercial
    /// customer database) would otherwise capture this publish into that context
    /// and never deliver it, since this flow only saves the financial context.
    /// The consumer is idempotent; a publish lost after the save committed is
    /// not retried here (the next resolve sees the stored value and no-ops), but
    /// the consumer's ledger clear is global, so any later
    /// <see cref="StockCusipChanged"/> from any stock re-imports the missed
    /// data sets and heals the gap.
    /// </para>
    /// </summary>
    public async Task SetCusip(CommonStock commonStock, string cusip)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        if (string.Equals(commonStock.Cusip, cusip, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var previousCusip = commonStock.Cusip;

        // The alias table enforces one CUSIP → one stock, ever (global unique
        // index): a retired CUSIP already recorded — even for another stock —
        // is left with its first owner rather than reassigned. The existence
        // check is case-insensitive so a case-variant CUSIP can't slip past
        // the index as a duplicate row.
        var normalizedPrevious = previousCusip?.ToUpperInvariant();
        if (
            previousCusip != null
            && !await _commonStockRepository
                .GetCusipAliases()
                .AnyAsync(a => a.Cusip.ToUpper() == normalizedPrevious)
        )
        {
            _commonStockRepository.AddCusipAlias(
                new CommonStockCusipAlias { CommonStockId = commonStock.Id, Cusip = previousCusip }
            );
        }

        commonStock.Cusip = cusip;

        await _commonStockRepository.SaveChanges();

        // Publish via the root bus (bypasses any bus outbox) after the write commits.
        await _bus.Publish(
            new StockCusipChanged(commonStock.Id, commonStock.Ticker, previousCusip, cusip)
        );
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

        if (day is not null && day > DateTime.DaysInMonth(2000, month))
        {
            throw new DomainValidationException($"Day {day} is invalid for month {month}");
        }

        if (commonStock.FiscalYearEndMonth == month && commonStock.FiscalYearEndDay == day)
        {
            return;
        }

        commonStock.FiscalYearEndMonth = month;
        commonStock.FiscalYearEndDay = day;
        await _commonStockRepository.SaveChanges();
    }

    /// <summary>
    /// Sets the company's SEC classification — the submissions <c>sic</c> code and
    /// <c>entityType</c> — used to tell operating companies apart from pooled
    /// investment vehicles. Blank values are normalised to null so a missing SIC
    /// stays eligible for a later refill rather than masquerading as classified. A
    /// no-op change persists nothing. Saves directly via the repository — like
    /// <see cref="SetFiscalYearEnd"/>, this mutates non-key fields and must not
    /// re-run the full ticker/CIK uniqueness validation.
    /// </summary>
    public async Task SetSecClassification(CommonStock commonStock, string sic, string entityType)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        var normalizedSic = string.IsNullOrWhiteSpace(sic) ? null : sic.Trim();
        var normalizedEntityType = string.IsNullOrWhiteSpace(entityType) ? null : entityType.Trim();

        if (commonStock.Sic == normalizedSic && commonStock.EntityType == normalizedEntityType)
        {
            return;
        }

        commonStock.Sic = normalizedSic;
        commonStock.EntityType = normalizedEntityType;
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
