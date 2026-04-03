using Equibles.Integrations.Yahoo.Models;

namespace Equibles.Integrations.Yahoo.Contracts;

public interface IYahooFinanceClient {
    Task<List<HistoricalPrice>> GetHistoricalPrices(string ticker, DateOnly startDate, DateOnly endDate);
    Task<List<RecommendationTrend>> GetRecommendationTrends(string ticker);
    Task<KeyStatistics> GetKeyStatistics(string ticker);
}
