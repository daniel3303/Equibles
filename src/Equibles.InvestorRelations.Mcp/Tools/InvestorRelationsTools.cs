using System.ComponentModel;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.BusinessLogic.Extensions;
using Equibles.Mcp;
using Equibles.Mcp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.InvestorRelations.Mcp.Tools;

[McpServerToolType]
public class InvestorRelationsTools
{
    private readonly CommonStockRepository _commonStockRepository;
    private readonly IrNewsItemRepository _newsRepository;
    private readonly IrEventRepository _eventRepository;
    private readonly McpToolRunner _runner;

    public InvestorRelationsTools(
        CommonStockRepository commonStockRepository,
        IrNewsItemRepository newsRepository,
        IrEventRepository eventRepository,
        ErrorManager errorManager,
        ILogger<InvestorRelationsTools> logger
    )
    {
        _commonStockRepository = commonStockRepository;
        _newsRepository = newsRepository;
        _eventRepository = eventRepository;
        _runner = new McpToolRunner(logger, errorManager.AsMcpErrorReporter());
    }

    [McpServerTool(Name = "GetInvestorRelationsNews")]
    [Description(
        "Get recent investor-relations press releases for a stock, scraped from the company's IR website. Returns the most recent news items (headline, publish date, and link) in reverse-chronological order. Use this to see a company's latest official announcements straight from its IR page, distinct from third-party news."
    )]
    public Task<string> GetInvestorRelationsNews(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Maximum number of news items to return (default: 20)")] int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                maxResults = McpLimit.Clamp(maxResults);
                var news = await _newsRepository.GetByStock(stock).Take(maxResults).ToListAsync();

                if (news.Count == 0)
                    return $"No investor relations news available for {ticker}.";

                var builder = new StringBuilder();
                builder.AppendLine($"Investor relations news for {stock.Ticker} ({stock.Name}):");
                builder.AppendLine();
                foreach (var item in news)
                {
                    builder.AppendLine($"- {item.PublishedAt:yyyy-MM-dd}: {item.Title}");
                    builder.AppendLine($"  {item.Url}");
                }

                return builder.ToString();
            },
            "GetInvestorRelationsNews",
            $"ticker: {ticker}"
        );
    }

    [McpServerTool(Name = "GetInvestorRelationsEvents")]
    [Description(
        "Get upcoming investor-relations events for a stock — earnings webcasts, conference appearances, presentations, and shareholder meetings — scraped from the company's IR website. Returns events scheduled from now onward, soonest first. Use this to see what an investor-relations program has on the calendar."
    )]
    public Task<string> GetInvestorRelationsEvents(
        [Description("Company ticker symbol (e.g., AAPL, MSFT)")] string ticker,
        [Description("Maximum number of events to return (default: 20)")] int maxResults = 20
    )
    {
        return _runner.Execute(
            async () =>
            {
                var (stock, stockError) = await _commonStockRepository.ResolveByTicker(ticker);
                if (stockError != null)
                    return stockError;

                maxResults = McpLimit.Clamp(maxResults);
                var now = DateTime.UtcNow;
                var events = await _eventRepository
                    .GetByStock(stock)
                    .Where(e => e.StartDateTime >= now)
                    .Take(maxResults)
                    .ToListAsync();

                if (events.Count == 0)
                    return $"No upcoming investor relations events available for {ticker}.";

                var builder = new StringBuilder();
                builder.AppendLine(
                    $"Upcoming investor relations events for {stock.Ticker} ({stock.Name}):"
                );
                builder.AppendLine();
                foreach (var ev in events)
                {
                    builder.AppendLine(
                        $"- {ev.StartDateTime:yyyy-MM-dd HH:mm} UTC [{EventTypeLabel(ev.Type)}]: {ev.Title}"
                    );
                    if (!string.IsNullOrEmpty(ev.Url))
                        builder.AppendLine($"  {ev.Url}");
                }

                return builder.ToString();
            },
            "GetInvestorRelationsEvents",
            $"ticker: {ticker}"
        );
    }

    private static string EventTypeLabel(IrEventType type) =>
        type switch
        {
            IrEventType.EarningsCall => "Earnings call",
            IrEventType.Conference => "Conference",
            IrEventType.Presentation => "Presentation",
            IrEventType.ShareholderMeeting => "Shareholder meeting",
            IrEventType.Webcast => "Webcast",
            _ => "Event",
        };
}
