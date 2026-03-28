using Equibles.Errors.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.Errors.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Congress.HostedService.Configuration;
using Equibles.Congress.HostedService.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Congress.HostedService.Services;

[Service]
public class CongressionalTradeSyncService {
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CongressionalTradeSyncService> _logger;
    private readonly CongressScraperOptions _options;
    private readonly WorkerOptions _workerOptions;

    public CongressionalTradeSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<CongressScraperOptions> options,
        IOptions<WorkerOptions> workerOptions,
        ILogger<CongressionalTradeSyncService> logger
    ) {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _workerOptions = workerOptions.Value;
        _logger = logger;
    }

    public async Task SyncAll(CancellationToken ct) {
        var fromDate = _workerOptions.MinSyncDate.HasValue
            ? DateOnly.FromDateTime(_workerOptions.MinSyncDate.Value)
            : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));
        var toDate = DateOnly.FromDateTime(DateTime.UtcNow);

        _logger.LogInformation("Starting congressional trade sync from {From} to {To}", fromDate, toDate);

        var allTransactions = new List<DisclosureTransaction>();

        await FetchSenateTransactions(allTransactions, fromDate, toDate, ct);
        await FetchHouseTransactions(allTransactions, fromDate, toDate, ct);

        if (allTransactions.Count == 0) {
            _logger.LogInformation("No congressional transactions found");
            return;
        }

        _logger.LogInformation("Fetched {Count} total congressional transactions, matching to tracked stocks",
            allTransactions.Count);

        await ProcessTransactions(allTransactions, ct);
    }

    private async Task FetchSenateTransactions(
        List<DisclosureTransaction> target, DateOnly from, DateOnly to, CancellationToken ct
    ) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<SenateDisclosureClient>();
            var txns = await client.GetRecentTransactions(from, to, ct);
            target.AddRange(txns);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to fetch Senate disclosure data");
            await ReportError("CongressTrades.SyncSenate", ex.Message, ex.StackTrace);
        }
    }

    private async Task FetchHouseTransactions(
        List<DisclosureTransaction> target, DateOnly from, DateOnly to, CancellationToken ct
    ) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<HouseDisclosureClient>();
            var txns = await client.GetRecentTransactions(from, to, ct);
            target.AddRange(txns);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to fetch House disclosure data");
            await ReportError("CongressTrades.SyncHouse", ex.Message, ex.StackTrace);
        }
    }

    private async Task ProcessTransactions(List<DisclosureTransaction> transactions, CancellationToken ct) {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var memberRepository = scope.ServiceProvider.GetRequiredService<CongressMemberRepository>();
        var tradeRepository = scope.ServiceProvider.GetRequiredService<CongressionalTradeRepository>();
        var commonStockRepository = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var stockQuery = _options.TickersToSync?.Count > 0
            ? commonStockRepository.GetByTickers(_options.TickersToSync)
            : commonStockRepository.GetAll();

        var stocks = await stockQuery.AsNoTracking()
            .ToDictionaryAsync(s => s.Ticker, s => s, StringComparer.OrdinalIgnoreCase, ct);

        var matched = transactions
            .Where(t => !string.IsNullOrEmpty(t.Ticker) && stocks.ContainsKey(t.Ticker))
            .ToList();

        _logger.LogInformation("Matched {Matched}/{Total} transactions to tracked stocks",
            matched.Count, transactions.Count);

        if (matched.Count == 0) return;

        var members = await UpsertCongressMembers(matched, memberRepository, ct);
        var trades = BuildTrades(matched, members, stocks);
        await PersistTrades(trades, tradeRepository, ct);
    }

    private async Task<Dictionary<string, CongressMember>> UpsertCongressMembers(
        List<DisclosureTransaction> matched, CongressMemberRepository memberRepository, CancellationToken ct
    ) {
        var distinctMembers = matched
            .GroupBy(t => t.MemberName)
            .Select(g => g.First())
            .Select(t => new CongressMember { Name = t.MemberName, Position = t.Position })
            .ToList();

        await memberRepository.GetDbSet()
            .UpsertRange(distinctMembers)
            .On(m => new { m.Name })
            .WhenMatched(m => new CongressMember { Position = m.Position })
            .RunAsync(ct);

        var memberNames = distinctMembers.Select(m => m.Name).ToList();
        return await memberRepository.GetAll()
            .Where(m => memberNames.Contains(m.Name))
            .ToDictionaryAsync(m => m.Name, ct);
    }

    private List<CongressionalTrade> BuildTrades(
        List<DisclosureTransaction> matched,
        Dictionary<string, CongressMember> members,
        Dictionary<string, CommonStock> stocks
    ) {
        var trades = new List<CongressionalTrade>();

        foreach (var tx in matched) {
            if (!members.TryGetValue(tx.MemberName, out var member)) {
                _logger.LogWarning("Congress member not found after upsert: {Name}", tx.MemberName);
                continue;
            }

            var stock = stocks[tx.Ticker];

            trades.Add(new CongressionalTrade {
                CongressMemberId = member.Id,
                CommonStockId = stock.Id,
                TransactionDate = tx.TransactionDate,
                FilingDate = tx.FilingDate,
                TransactionType = tx.TransactionType,
                OwnerType = tx.OwnerType,
                AssetName = tx.AssetName ?? "",
                AmountFrom = tx.AmountFrom,
                AmountTo = tx.AmountTo,
            });
        }

        return trades;
    }

    private async Task PersistTrades(
        List<CongressionalTrade> trades,
        CongressionalTradeRepository tradeRepository,
        CancellationToken ct
    ) {
        await tradeRepository.GetDbSet()
            .UpsertRange(trades)
            .On(t => new { t.CommonStockId, t.CongressMemberId, t.TransactionDate, t.TransactionType, t.AssetName })
            .NoUpdate()
            .RunAsync(ct);

        _logger.LogInformation("Upserted {Count} congressional trades (duplicates skipped)", trades.Count);
    }

    private async Task ReportError(string context, string message, string stackTrace, string requestSummary = null) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var errorManager = scope.ServiceProvider.GetRequiredService<ErrorManager>();
            await errorManager.Create(ErrorSource.CongressScraper, context, message, stackTrace, requestSummary);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to report error for {Context}", context);
        }
    }
}
