using Equibles.Errors.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.Errors.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Congress.HostedService.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Congress.HostedService.Services;

[Service]
public class CongressionalTradeSyncService {
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CongressionalTradeSyncService> _logger;
    private readonly WorkerOptions _workerOptions;
    private readonly ErrorReporter _errorReporter;

    public CongressionalTradeSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerOptions> workerOptions,
        ILogger<CongressionalTradeSyncService> logger,
        ErrorReporter errorReporter
    ) {
        _scopeFactory = scopeFactory;
        _workerOptions = workerOptions.Value;
        _logger = logger;
        _errorReporter = errorReporter;
    }

    // Congressional trade disclosures are available from 2012 (STOCK Act).
    private static readonly DateOnly EarliestAvailableDate = new(2012, 4, 1);

    public async Task SyncAll(CancellationToken ct) {
        var fromDate = _workerOptions.MinSyncDate.HasValue
            ? DateOnly.FromDateTime(_workerOptions.MinSyncDate.Value)
            : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));

        if (fromDate < EarliestAvailableDate)
            fromDate = EarliestAvailableDate;
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
            await _errorReporter.Report(ErrorSource.CongressScraper, "CongressTrades.SyncSenate", ex.Message, ex.StackTrace);
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
            await _errorReporter.Report(ErrorSource.CongressScraper, "CongressTrades.SyncHouse", ex.Message, ex.StackTrace);
        }
    }

    private async Task ProcessTransactions(List<DisclosureTransaction> transactions, CancellationToken ct) {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();
        var memberRepository = scope.ServiceProvider.GetRequiredService<CongressMemberRepository>();
        var tradeRepository = scope.ServiceProvider.GetRequiredService<CongressionalTradeRepository>();
        var commonStockRepository = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var stockQuery = _workerOptions.TickersToSync?.Count > 0
            ? commonStockRepository.GetByTickers(_workerOptions.TickersToSync)
            : commonStockRepository.GetAll();

        var stocks = await stockQuery.AsNoTracking()
            .ToDictionaryAsync(s => s.Ticker, s => s, StringComparer.OrdinalIgnoreCase, ct);

        var matched = transactions
            .Where(t => !string.IsNullOrEmpty(t.Ticker) && stocks.ContainsKey(t.Ticker))
            .ToList();

        _logger.LogInformation("Matched {Matched}/{Total} transactions to tracked stocks",
            matched.Count, transactions.Count);

        if (matched.Count == 0) return;

        var members = await UpsertCongressMembers(matched, dbContext, memberRepository, ct);
        var trades = BuildTrades(matched, members, stocks);
        await PersistTrades(trades, dbContext, ct);
    }

    private async Task<Dictionary<string, CongressMember>> UpsertCongressMembers(
        List<DisclosureTransaction> matched, EquiblesDbContext dbContext, CongressMemberRepository memberRepository, CancellationToken ct
    ) {
        var distinctMembers = matched
            .GroupBy(t => t.MemberName)
            .Select(g => g.First())
            .Select(t => new CongressMember { Name = t.MemberName, Position = t.Position })
            .ToList();

        await dbContext.Set<CongressMember>()
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
        EquiblesDbContext dbContext,
        CancellationToken ct
    ) {
        await dbContext.Set<CongressionalTrade>()
            .UpsertRange(trades)
            .On(t => new { t.CommonStockId, t.CongressMemberId, t.TransactionDate, t.TransactionType, t.AssetName })
            .NoUpdate()
            .RunAsync(ct);

        _logger.LogInformation("Upserted {Count} congressional trades (duplicates skipped)", trades.Count);
    }

}
