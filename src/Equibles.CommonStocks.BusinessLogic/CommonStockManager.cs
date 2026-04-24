using Equibles.Core.AutoWiring;
using Equibles.Core.Exceptions;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;

namespace Equibles.CommonStocks.BusinessLogic;

[Service]
public class CommonStockManager {
    private readonly CommonStockRepository _commonStockRepository;

    public CommonStockManager(CommonStockRepository commonStockRepository) {
        _commonStockRepository = commonStockRepository;
    }

    public async Task<CommonStock> Create(CommonStock commonStock) {
        await ValidateCommonStock(commonStock, true);
        _commonStockRepository.Add(commonStock);
        await _commonStockRepository.SaveChanges();
        return commonStock;
    }

    public async Task<CommonStock> Update(CommonStock commonStock) {
        await ValidateCommonStock(commonStock, false);
        await _commonStockRepository.SaveChanges();
        return commonStock;
    }

    private async Task ValidateCommonStock(CommonStock commonStock, bool isInsert) {
        if (commonStock == null) {
            throw new ArgumentNullException(nameof(commonStock));
        }

        // Checks for the required fields
        if (string.IsNullOrEmpty(commonStock.Ticker)) {
            throw new DomainValidationException("Ticker is required");
        }

        if (string.IsNullOrEmpty(commonStock.Name)) {
            throw new DomainValidationException("Name is required");
        }

        if (string.IsNullOrEmpty(commonStock.Cik)) {
            throw new DomainValidationException("Cik is required");
        }

        if (commonStock.MarketCapitalization < 0) {
            throw new DomainValidationException("MarketCapitalization cannot be negative");
        }

        if (commonStock.SharesOutStanding < 0) {
            throw new DomainValidationException("SharesOutStanding cannot be negative");
        }

        // Primary ticker must be globally unique across all companies.
        var existingByTicker = await _commonStockRepository.GetByPrimaryTicker(commonStock.Ticker);
        if (existingByTicker != null && (isInsert || existingByTicker.Id != commonStock.Id)) {
            throw new DomainValidationException($"CommonStock with ticker {commonStock.Ticker} already exists");
        }

        var existingByCik = await _commonStockRepository.GetByCik(commonStock.Cik);
        if (existingByCik != null && (isInsert || existingByCik.Id != commonStock.Id)) {
            throw new DomainValidationException($"CommonStock with cik {commonStock.Cik} already exists");
        }

        // Secondary tickers are allowed to overlap with primary or secondary tickers of other
        // companies. In SEC filings a preferred-share ticker can legitimately appear under both
        // the parent REIT filer and its operating-partnership filer, so cross-company overlap
        // is valid. Lookups resolve ambiguity via GetByTicker's primary-first ordering.
    }
}