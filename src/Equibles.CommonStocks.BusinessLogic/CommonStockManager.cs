using Equibles.Core.AutoWiring;
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
            throw new ApplicationException("Ticker is required");
        }

        if (string.IsNullOrEmpty(commonStock.Name)) {
            throw new ApplicationException("Name is required");
        }

        if (string.IsNullOrEmpty(commonStock.Cik)) {
            throw new ApplicationException("Cik is required");
        }

        if (commonStock.MarketCapitalization < 0) {
            throw new ApplicationException("MarketCapitalization cannot be negative");
        }

        if (commonStock.SharesOutStanding < 0) {
            throw new ApplicationException("SharesOutStanding cannot be negative");
        }

        // Checks for unique indexes
        var existingByTicker = await _commonStockRepository.GetByTicker(commonStock.Ticker);
        if (existingByTicker != null && (isInsert || existingByTicker.Id != commonStock.Id)) {
            throw new ApplicationException($"CommonStock with ticker {commonStock.Ticker} already exists");
        }

        var existingByCik = await _commonStockRepository.GetByCik(commonStock.Cik);
        if (existingByCik != null && (isInsert || existingByCik.Id != commonStock.Id)) {
            throw new ApplicationException($"CommonStock with cik {commonStock.Cik} already exists");
        }

        // Check secondary tickers don't collide with other companies' primary or secondary tickers
        foreach (var secondaryTicker in commonStock.SecondaryTickers) {
            var existingBySecondary = await _commonStockRepository.GetByTicker(secondaryTicker);
            if (existingBySecondary != null && existingBySecondary.Id != commonStock.Id) {
                throw new ApplicationException($"Secondary ticker {secondaryTicker} is already used as a primary ticker by another company");
            }
        }
    }


}