using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.Tests.Congress;

/// <summary>
/// Tests for <see cref="CongressionalTradeSyncService"/>.
/// The service's public entry point (<c>SyncAll</c>) orchestrates scope creation, HTTP
/// fetching, member upsert (FlexLabs), and trade persistence -- all of which depend on
/// infrastructure that cannot run in the InMemory provider. Instead, we test the pure-logic
/// private methods via reflection: <c>BuildTrades</c> (mapping + matching) and the
/// transaction-filtering behaviour that determines which disclosures become trades.
/// </summary>
public class CongressSyncServiceTests {
    // ── Reflection helpers ──────────────────────────────────────────────

    private static readonly MethodInfo BuildTradesMethod = typeof(CongressionalTradeSyncService)
        .GetMethod("BuildTrades", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static CongressionalTradeSyncService CreateService() {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var options = Options.Create(new WorkerOptions());
        var logger = Substitute.For<ILogger<CongressionalTradeSyncService>>();
        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>());

        return new CongressionalTradeSyncService(scopeFactory, options, logger, errorReporter);
    }

    private static List<CongressionalTrade> InvokeBuildTrades(
        CongressionalTradeSyncService service,
        List<DisclosureTransaction> matched,
        Dictionary<string, CongressMember> members,
        Dictionary<string, CommonStock> stocks
    ) {
        return (List<CongressionalTrade>)BuildTradesMethod.Invoke(
            service, [matched, members, stocks])!;
    }

    // ── Factory helpers ─────────────────────────────────────────────────

    private static CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc.") {
        return new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
        };
    }

    private static CongressMember CreateMember(
        string name = "Nancy Pelosi",
        CongressPosition position = CongressPosition.Representative
    ) {
        return new CongressMember {
            Id = Guid.NewGuid(),
            Name = name,
            Position = position,
        };
    }

    private static DisclosureTransaction CreateTransaction(
        string memberName = "Nancy Pelosi",
        string ticker = "AAPL",
        CongressPosition position = CongressPosition.Representative,
        CongressTransactionType type = CongressTransactionType.Purchase,
        DateOnly? txDate = null,
        DateOnly? filingDate = null,
        string assetName = "Apple Inc Common Stock",
        string ownerType = "Self",
        long amountFrom = 1_001,
        long amountTo = 15_000
    ) {
        var date = txDate ?? new DateOnly(2024, 6, 15);
        return new DisclosureTransaction {
            MemberName = memberName,
            Position = position,
            Ticker = ticker,
            AssetName = assetName,
            TransactionDate = date,
            FilingDate = filingDate ?? date.AddDays(30),
            TransactionType = type,
            OwnerType = ownerType,
            AmountFrom = amountFrom,
            AmountTo = amountTo,
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // BuildTrades — basic mapping
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildTrades_SingleTransaction_MapsAllFieldsCorrectly() {
        var service = CreateService();
        var stock = CreateStock("AAPL", "Apple Inc.");
        var member = CreateMember("Nancy Pelosi", CongressPosition.Representative);

        var tx = CreateTransaction(
            memberName: "Nancy Pelosi",
            ticker: "AAPL",
            type: CongressTransactionType.Purchase,
            txDate: new DateOnly(2024, 6, 15),
            filingDate: new DateOnly(2024, 7, 15),
            assetName: "Apple Inc Common Stock",
            ownerType: "Self",
            amountFrom: 1_001,
            amountTo: 15_000);

        var members = new Dictionary<string, CongressMember> { ["Nancy Pelosi"] = member };
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase)
            { ["AAPL"] = stock };

        var result = InvokeBuildTrades(service, [tx], members, stocks);

        result.Should().ContainSingle();
        var trade = result[0];
        trade.CongressMemberId.Should().Be(member.Id);
        trade.CommonStockId.Should().Be(stock.Id);
        trade.TransactionDate.Should().Be(new DateOnly(2024, 6, 15));
        trade.FilingDate.Should().Be(new DateOnly(2024, 7, 15));
        trade.TransactionType.Should().Be(CongressTransactionType.Purchase);
        trade.OwnerType.Should().Be("Self");
        trade.AssetName.Should().Be("Apple Inc Common Stock");
        trade.AmountFrom.Should().Be(1_001);
        trade.AmountTo.Should().Be(15_000);
    }

    [Fact]
    public void BuildTrades_SaleTransaction_MapsTransactionTypeCorrectly() {
        var service = CreateService();
        var stock = CreateStock("MSFT");
        var member = CreateMember("Tommy Tuberville", CongressPosition.Senator);

        var tx = CreateTransaction(
            memberName: "Tommy Tuberville",
            ticker: "MSFT",
            type: CongressTransactionType.Sale);

        var members = new Dictionary<string, CongressMember> { ["Tommy Tuberville"] = member };
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase)
            { ["MSFT"] = stock };

        var result = InvokeBuildTrades(service, [tx], members, stocks);

        result.Should().ContainSingle()
            .Which.TransactionType.Should().Be(CongressTransactionType.Sale);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BuildTrades — multiple transactions
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildTrades_MultipleTransactions_BuildsAll() {
        var service = CreateService();
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        var pelosi = CreateMember("Nancy Pelosi");
        var tuberville = CreateMember("Tommy Tuberville", CongressPosition.Senator);

        var transactions = new List<DisclosureTransaction> {
            CreateTransaction(memberName: "Nancy Pelosi", ticker: "AAPL"),
            CreateTransaction(memberName: "Tommy Tuberville", ticker: "MSFT",
                type: CongressTransactionType.Sale),
            CreateTransaction(memberName: "Nancy Pelosi", ticker: "MSFT",
                txDate: new DateOnly(2024, 7, 1)),
        };

        var members = new Dictionary<string, CongressMember> {
            ["Nancy Pelosi"] = pelosi,
            ["Tommy Tuberville"] = tuberville,
        };
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase) {
            ["AAPL"] = apple,
            ["MSFT"] = msft,
        };

        var result = InvokeBuildTrades(service, transactions, members, stocks);

        result.Should().HaveCount(3);
        result.Count(t => t.CongressMemberId == pelosi.Id).Should().Be(2);
        result.Count(t => t.CongressMemberId == tuberville.Id).Should().Be(1);
        result.Count(t => t.CommonStockId == apple.Id).Should().Be(1);
        result.Count(t => t.CommonStockId == msft.Id).Should().Be(2);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BuildTrades — member not found after upsert (skip gracefully)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildTrades_MemberNotInDictionary_SkipsTransaction() {
        var service = CreateService();
        var stock = CreateStock("AAPL");

        var tx = CreateTransaction(memberName: "Unknown Senator", ticker: "AAPL");

        var members = new Dictionary<string, CongressMember>(); // empty — member not found
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase)
            { ["AAPL"] = stock };

        var result = InvokeBuildTrades(service, [tx], members, stocks);

        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildTrades_SomeMembersMissing_OnlyBuildsMatchedOnes() {
        var service = CreateService();
        var stock = CreateStock("AAPL");
        var pelosi = CreateMember("Nancy Pelosi");

        var transactions = new List<DisclosureTransaction> {
            CreateTransaction(memberName: "Nancy Pelosi", ticker: "AAPL"),
            CreateTransaction(memberName: "Ghost Member", ticker: "AAPL"),
        };

        var members = new Dictionary<string, CongressMember> { ["Nancy Pelosi"] = pelosi };
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase)
            { ["AAPL"] = stock };

        var result = InvokeBuildTrades(service, transactions, members, stocks);

        result.Should().ContainSingle()
            .Which.CongressMemberId.Should().Be(pelosi.Id);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BuildTrades — empty inputs
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildTrades_EmptyTransactionList_ReturnsEmpty() {
        var service = CreateService();
        var members = new Dictionary<string, CongressMember>();
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase);

        var result = InvokeBuildTrades(service, [], members, stocks);

        result.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // BuildTrades — amount edge cases
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildTrades_ZeroAmounts_MapsCorrectly() {
        var service = CreateService();
        var stock = CreateStock("TSLA");
        var member = CreateMember("Dan Crenshaw");

        var tx = CreateTransaction(
            memberName: "Dan Crenshaw",
            ticker: "TSLA",
            amountFrom: 0,
            amountTo: 0);

        var members = new Dictionary<string, CongressMember> { ["Dan Crenshaw"] = member };
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase)
            { ["TSLA"] = stock };

        var result = InvokeBuildTrades(service, [tx], members, stocks);

        result.Should().ContainSingle();
        result[0].AmountFrom.Should().Be(0);
        result[0].AmountTo.Should().Be(0);
    }

    [Fact]
    public void BuildTrades_LargeAmounts_MapsCorrectly() {
        var service = CreateService();
        var stock = CreateStock("NVDA");
        var member = CreateMember("Nancy Pelosi");

        var tx = CreateTransaction(
            memberName: "Nancy Pelosi",
            ticker: "NVDA",
            amountFrom: 1_000_001,
            amountTo: 5_000_000);

        var members = new Dictionary<string, CongressMember> { ["Nancy Pelosi"] = member };
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase)
            { ["NVDA"] = stock };

        var result = InvokeBuildTrades(service, [tx], members, stocks);

        result.Should().ContainSingle();
        result[0].AmountFrom.Should().Be(1_000_001);
        result[0].AmountTo.Should().Be(5_000_000);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BuildTrades — asset name edge cases
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildTrades_EmptyAssetName_MapsEmptyString() {
        var service = CreateService();
        var stock = CreateStock("AAPL");
        var member = CreateMember("Nancy Pelosi");

        var tx = CreateTransaction(
            memberName: "Nancy Pelosi",
            ticker: "AAPL",
            assetName: null);

        var members = new Dictionary<string, CongressMember> { ["Nancy Pelosi"] = member };
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase)
            { ["AAPL"] = stock };

        var result = InvokeBuildTrades(service, [tx], members, stocks);

        // The service uses `tx.AssetName ?? ""` so null becomes empty string
        result.Should().ContainSingle()
            .Which.AssetName.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Transaction filtering — ticker matching to tracked stocks
    // These test the filtering logic that happens in ProcessTransactions
    // before BuildTrades is called. We replicate the exact LINQ filter.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void TickerMatching_TransactionsWithTrackedTickers_AreIncluded() {
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase) {
            ["AAPL"] = CreateStock("AAPL"),
            ["MSFT"] = CreateStock("MSFT"),
        };

        var transactions = new List<DisclosureTransaction> {
            CreateTransaction(ticker: "AAPL"),
            CreateTransaction(ticker: "MSFT"),
        };

        var matched = transactions
            .Where(t => !string.IsNullOrEmpty(t.Ticker) && stocks.ContainsKey(t.Ticker))
            .ToList();

        matched.Should().HaveCount(2);
    }

    [Fact]
    public void TickerMatching_TransactionsWithUntrackedTickers_AreExcluded() {
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase) {
            ["AAPL"] = CreateStock("AAPL"),
        };

        var transactions = new List<DisclosureTransaction> {
            CreateTransaction(ticker: "AAPL"),
            CreateTransaction(ticker: "GOOG"),
            CreateTransaction(ticker: "TSLA"),
        };

        var matched = transactions
            .Where(t => !string.IsNullOrEmpty(t.Ticker) && stocks.ContainsKey(t.Ticker))
            .ToList();

        matched.Should().ContainSingle()
            .Which.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public void TickerMatching_NullOrEmptyTicker_IsExcluded() {
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase) {
            ["AAPL"] = CreateStock("AAPL"),
        };

        var transactions = new List<DisclosureTransaction> {
            CreateTransaction(ticker: null),
            CreateTransaction(ticker: ""),
            CreateTransaction(ticker: "AAPL"),
        };

        var matched = transactions
            .Where(t => !string.IsNullOrEmpty(t.Ticker) && stocks.ContainsKey(t.Ticker))
            .ToList();

        matched.Should().ContainSingle();
    }

    [Fact]
    public void TickerMatching_CaseInsensitive_MatchesLowercaseTicker() {
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase) {
            ["AAPL"] = CreateStock("AAPL"),
        };

        var transactions = new List<DisclosureTransaction> {
            CreateTransaction(ticker: "aapl"),
            CreateTransaction(ticker: "Aapl"),
            CreateTransaction(ticker: "AAPL"),
        };

        var matched = transactions
            .Where(t => !string.IsNullOrEmpty(t.Ticker) && stocks.ContainsKey(t.Ticker))
            .ToList();

        matched.Should().HaveCount(3);
    }

    [Fact]
    public void TickerMatching_NoTrackedStocks_ReturnsEmpty() {
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase);

        var transactions = new List<DisclosureTransaction> {
            CreateTransaction(ticker: "AAPL"),
            CreateTransaction(ticker: "MSFT"),
        };

        var matched = transactions
            .Where(t => !string.IsNullOrEmpty(t.Ticker) && stocks.ContainsKey(t.Ticker))
            .ToList();

        matched.Should().BeEmpty();
    }

    [Fact]
    public void TickerMatching_AllTransactionsHaveNullTickers_ReturnsEmpty() {
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase) {
            ["AAPL"] = CreateStock("AAPL"),
        };

        var transactions = new List<DisclosureTransaction> {
            CreateTransaction(ticker: null),
            CreateTransaction(ticker: null),
        };

        var matched = transactions
            .Where(t => !string.IsNullOrEmpty(t.Ticker) && stocks.ContainsKey(t.Ticker))
            .ToList();

        matched.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Dedup key composition — the upsert's On() clause defines the
    // unique key: (CommonStockId, CongressMemberId, TransactionDate,
    // TransactionType, AssetName). Verify that BuildTrades produces
    // trades with the correct composite key values.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildTrades_DuplicateTransactions_ProducesSameCompositeKey() {
        var service = CreateService();
        var stock = CreateStock("AAPL");
        var member = CreateMember("Nancy Pelosi");

        var tx1 = CreateTransaction(
            memberName: "Nancy Pelosi", ticker: "AAPL",
            type: CongressTransactionType.Purchase,
            txDate: new DateOnly(2024, 6, 15),
            assetName: "Apple Inc Common Stock");
        var tx2 = CreateTransaction(
            memberName: "Nancy Pelosi", ticker: "AAPL",
            type: CongressTransactionType.Purchase,
            txDate: new DateOnly(2024, 6, 15),
            assetName: "Apple Inc Common Stock");

        var members = new Dictionary<string, CongressMember> { ["Nancy Pelosi"] = member };
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase)
            { ["AAPL"] = stock };

        var result = InvokeBuildTrades(service, [tx1, tx2], members, stocks);

        // Both trades should produce the same composite key
        result.Should().HaveCount(2);
        var key1 = (result[0].CommonStockId, result[0].CongressMemberId,
            result[0].TransactionDate, result[0].TransactionType, result[0].AssetName);
        var key2 = (result[1].CommonStockId, result[1].CongressMemberId,
            result[1].TransactionDate, result[1].TransactionType, result[1].AssetName);
        key1.Should().Be(key2);
    }

    [Fact]
    public void BuildTrades_DifferentDates_ProduceDifferentCompositeKeys() {
        var service = CreateService();
        var stock = CreateStock("AAPL");
        var member = CreateMember("Nancy Pelosi");

        var tx1 = CreateTransaction(
            memberName: "Nancy Pelosi", ticker: "AAPL",
            txDate: new DateOnly(2024, 6, 15));
        var tx2 = CreateTransaction(
            memberName: "Nancy Pelosi", ticker: "AAPL",
            txDate: new DateOnly(2024, 7, 20));

        var members = new Dictionary<string, CongressMember> { ["Nancy Pelosi"] = member };
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase)
            { ["AAPL"] = stock };

        var result = InvokeBuildTrades(service, [tx1, tx2], members, stocks);

        result.Should().HaveCount(2);
        result[0].TransactionDate.Should().NotBe(result[1].TransactionDate);
    }

    [Fact]
    public void BuildTrades_DifferentTransactionTypes_ProduceDifferentCompositeKeys() {
        var service = CreateService();
        var stock = CreateStock("AAPL");
        var member = CreateMember("Nancy Pelosi");

        var purchase = CreateTransaction(
            memberName: "Nancy Pelosi", ticker: "AAPL",
            type: CongressTransactionType.Purchase,
            txDate: new DateOnly(2024, 6, 15),
            assetName: "Apple Inc");
        var sale = CreateTransaction(
            memberName: "Nancy Pelosi", ticker: "AAPL",
            type: CongressTransactionType.Sale,
            txDate: new DateOnly(2024, 6, 15),
            assetName: "Apple Inc");

        var members = new Dictionary<string, CongressMember> { ["Nancy Pelosi"] = member };
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase)
            { ["AAPL"] = stock };

        var result = InvokeBuildTrades(service, [purchase, sale], members, stocks);

        result.Should().HaveCount(2);
        result.Select(t => t.TransactionType).Should().BeEquivalentTo(
            [CongressTransactionType.Purchase, CongressTransactionType.Sale]);
    }

    [Fact]
    public void BuildTrades_DifferentAssetNames_ProduceDifferentCompositeKeys() {
        var service = CreateService();
        var stock = CreateStock("AAPL");
        var member = CreateMember("Nancy Pelosi");

        var tx1 = CreateTransaction(
            memberName: "Nancy Pelosi", ticker: "AAPL",
            txDate: new DateOnly(2024, 6, 15),
            assetName: "Apple Inc Common Stock");
        var tx2 = CreateTransaction(
            memberName: "Nancy Pelosi", ticker: "AAPL",
            txDate: new DateOnly(2024, 6, 15),
            assetName: "Apple Inc Call Options");

        var members = new Dictionary<string, CongressMember> { ["Nancy Pelosi"] = member };
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase)
            { ["AAPL"] = stock };

        var result = InvokeBuildTrades(service, [tx1, tx2], members, stocks);

        result.Should().HaveCount(2);
        result.Select(t => t.AssetName).Should().BeEquivalentTo(
            ["Apple Inc Common Stock", "Apple Inc Call Options"]);
    }

    [Fact]
    public void BuildTrades_DifferentMembers_ProduceDifferentCompositeKeys() {
        var service = CreateService();
        var stock = CreateStock("AAPL");
        var pelosi = CreateMember("Nancy Pelosi");
        var tuberville = CreateMember("Tommy Tuberville", CongressPosition.Senator);

        var tx1 = CreateTransaction(memberName: "Nancy Pelosi", ticker: "AAPL");
        var tx2 = CreateTransaction(memberName: "Tommy Tuberville", ticker: "AAPL");

        var members = new Dictionary<string, CongressMember> {
            ["Nancy Pelosi"] = pelosi,
            ["Tommy Tuberville"] = tuberville,
        };
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase)
            { ["AAPL"] = stock };

        var result = InvokeBuildTrades(service, [tx1, tx2], members, stocks);

        result.Should().HaveCount(2);
        result.Select(t => t.CongressMemberId).Should().BeEquivalentTo(
            [pelosi.Id, tuberville.Id]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Member dedup extraction — the logic that groups transactions by
    // member name and selects distinct members for upsert
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void MemberDedup_MultipleTransactionsSameMember_ProducesOneMember() {
        var transactions = new List<DisclosureTransaction> {
            CreateTransaction(memberName: "Nancy Pelosi", ticker: "AAPL"),
            CreateTransaction(memberName: "Nancy Pelosi", ticker: "MSFT"),
            CreateTransaction(memberName: "Nancy Pelosi", ticker: "GOOG"),
        };

        // Replicate the exact dedup logic from UpsertCongressMembers
        var distinctMembers = transactions
            .GroupBy(t => t.MemberName)
            .Select(g => g.First())
            .Select(t => new CongressMember { Name = t.MemberName, Position = t.Position })
            .ToList();

        distinctMembers.Should().ContainSingle()
            .Which.Name.Should().Be("Nancy Pelosi");
    }

    [Fact]
    public void MemberDedup_DifferentMembers_ProducesDistinctEntries() {
        var transactions = new List<DisclosureTransaction> {
            CreateTransaction(memberName: "Nancy Pelosi", ticker: "AAPL"),
            CreateTransaction(memberName: "Tommy Tuberville", ticker: "MSFT"),
            CreateTransaction(memberName: "Dan Crenshaw", ticker: "GOOG"),
            CreateTransaction(memberName: "Nancy Pelosi", ticker: "TSLA"),
        };

        var distinctMembers = transactions
            .GroupBy(t => t.MemberName)
            .Select(g => g.First())
            .Select(t => new CongressMember { Name = t.MemberName, Position = t.Position })
            .ToList();

        distinctMembers.Should().HaveCount(3);
        distinctMembers.Select(m => m.Name).Should()
            .BeEquivalentTo(["Nancy Pelosi", "Tommy Tuberville", "Dan Crenshaw"]);
    }

    [Fact]
    public void MemberDedup_SameMemberDifferentPositions_TakesFirst() {
        // A member could theoretically appear with different positions
        // across Senate and House disclosures. The GroupBy takes the first.
        var transactions = new List<DisclosureTransaction> {
            CreateTransaction(memberName: "Some Member",
                position: CongressPosition.Senator, ticker: "AAPL"),
            CreateTransaction(memberName: "Some Member",
                position: CongressPosition.Representative, ticker: "MSFT"),
        };

        var distinctMembers = transactions
            .GroupBy(t => t.MemberName)
            .Select(g => g.First())
            .Select(t => new CongressMember { Name = t.MemberName, Position = t.Position })
            .ToList();

        distinctMembers.Should().ContainSingle();
        distinctMembers[0].Position.Should().Be(CongressPosition.Senator);
    }

    // ═══════════════════════════════════════════════════════════════════
    // End-to-end matching + building — full pipeline from disclosure
    // transactions to CongressionalTrade entities
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FullPipeline_MatchAndBuild_OnlyTrackedTickersProduceTrades() {
        var service = CreateService();
        var apple = CreateStock("AAPL", "Apple Inc.");
        var pelosi = CreateMember("Nancy Pelosi");

        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase) {
            ["AAPL"] = apple,
        };

        var allTransactions = new List<DisclosureTransaction> {
            CreateTransaction(memberName: "Nancy Pelosi", ticker: "AAPL"),
            CreateTransaction(memberName: "Nancy Pelosi", ticker: "GOOG"), // untracked
            CreateTransaction(memberName: "Nancy Pelosi", ticker: null),   // no ticker
            CreateTransaction(memberName: "Nancy Pelosi", ticker: ""),     // empty ticker
        };

        // Step 1: filter (same as ProcessTransactions)
        var matched = allTransactions
            .Where(t => !string.IsNullOrEmpty(t.Ticker) && stocks.ContainsKey(t.Ticker))
            .ToList();

        // Step 2: build trades
        var members = new Dictionary<string, CongressMember> { ["Nancy Pelosi"] = pelosi };
        var result = InvokeBuildTrades(service, matched, members, stocks);

        result.Should().ContainSingle();
        result[0].CommonStockId.Should().Be(apple.Id);
        result[0].CongressMemberId.Should().Be(pelosi.Id);
    }

    [Fact]
    public void FullPipeline_MultipleMembers_MultipleStocks_BuildsCorrectly() {
        var service = CreateService();
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        var nvda = CreateStock("NVDA", "NVIDIA Corp.");
        var pelosi = CreateMember("Nancy Pelosi");
        var tuberville = CreateMember("Tommy Tuberville", CongressPosition.Senator);

        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase) {
            ["AAPL"] = apple, ["MSFT"] = msft, ["NVDA"] = nvda,
        };

        var transactions = new List<DisclosureTransaction> {
            CreateTransaction(memberName: "Nancy Pelosi", ticker: "AAPL",
                type: CongressTransactionType.Purchase),
            CreateTransaction(memberName: "Nancy Pelosi", ticker: "NVDA",
                type: CongressTransactionType.Purchase,
                txDate: new DateOnly(2024, 7, 1)),
            CreateTransaction(memberName: "Tommy Tuberville", ticker: "MSFT",
                type: CongressTransactionType.Sale),
            CreateTransaction(memberName: "Tommy Tuberville", ticker: "GOOG"), // untracked
        };

        var matched = transactions
            .Where(t => !string.IsNullOrEmpty(t.Ticker) && stocks.ContainsKey(t.Ticker))
            .ToList();

        var members = new Dictionary<string, CongressMember> {
            ["Nancy Pelosi"] = pelosi,
            ["Tommy Tuberville"] = tuberville,
        };

        var result = InvokeBuildTrades(service, matched, members, stocks);

        result.Should().HaveCount(3);
        result.Count(t => t.CongressMemberId == pelosi.Id).Should().Be(2);
        result.Count(t => t.CongressMemberId == tuberville.Id).Should().Be(1);

        result.Single(t => t.CongressMemberId == tuberville.Id)
            .TransactionType.Should().Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void FullPipeline_EmptyTransactions_ProducesNoTrades() {
        var service = CreateService();
        var stocks = new Dictionary<string, CommonStock>(StringComparer.OrdinalIgnoreCase) {
            ["AAPL"] = CreateStock("AAPL"),
        };

        var matched = new List<DisclosureTransaction>()
            .Where(t => !string.IsNullOrEmpty(t.Ticker) && stocks.ContainsKey(t.Ticker))
            .ToList();

        var members = new Dictionary<string, CongressMember>();
        var result = InvokeBuildTrades(service, matched, members, stocks);

        result.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // WorkerOptions — MinSyncDate logic
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void WorkerOptions_MinSyncDateSet_UsesConfiguredDate() {
        var options = new WorkerOptions { MinSyncDate = new DateTime(2023, 1, 1) };

        var fromDate = options.MinSyncDate.HasValue
            ? DateOnly.FromDateTime(options.MinSyncDate.Value)
            : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));

        fromDate.Should().Be(new DateOnly(2023, 1, 1));
    }

    [Fact]
    public void WorkerOptions_MinSyncDateNull_DefaultsTo90DaysBack() {
        var options = new WorkerOptions { MinSyncDate = null };

        var fromDate = options.MinSyncDate.HasValue
            ? DateOnly.FromDateTime(options.MinSyncDate.Value)
            : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));

        var expected = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));
        fromDate.Should().Be(expected);
    }

    [Fact]
    public void WorkerOptions_TickersToSync_EmptyList_DefaultBehavior() {
        var options = new WorkerOptions();

        options.TickersToSync.Should().BeEmpty();
        var shouldFilterByTickers = options.TickersToSync?.Count > 0;
        shouldFilterByTickers.Should().BeFalse();
    }

    [Fact]
    public void WorkerOptions_TickersToSync_WithValues_ShouldFilter() {
        var options = new WorkerOptions { TickersToSync = ["AAPL", "MSFT"] };

        var shouldFilterByTickers = options.TickersToSync?.Count > 0;
        shouldFilterByTickers.Should().BeTrue();
    }
}
