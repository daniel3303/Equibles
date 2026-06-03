using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceAddOrMergeHoldingTests
{
    [Fact]
    public void AddOrMergeHolding_SecondRowWithSameKey_SumsQuantitiesAndKeepsOneEntry()
    {
        // Contract: a 13F filer can report the same (stock, holder, reportDate, shareType,
        // optionType, filingType) position across multiple manager blocks. The second row must
        // MERGE into the first — summing Shares/Value/voting authority and appending the manager
        // entry — leaving a single map entry and returning false. No existing test pins the merge;
        // a regression that overwrote (=) instead of summed (+=) would understate the position.
        var map = new Dictionary<string, InstitutionalHolding>();
        var stockId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

        InstitutionalHolding Holding(long shares, long value, long sole, long shared, long none) =>
            new()
            {
                CommonStockId = stockId,
                InstitutionalHolderId = holderId,
                ReportDate = reportDate,
                ShareType = ShareType.Shares,
                OptionType = null,
                FilingType = FilingType.Form13F,
                Shares = shares,
                Value = value,
                VotingAuthSole = sole,
                VotingAuthShared = shared,
                VotingAuthNone = none,
            };

        var method = typeof(HoldingsImportService).GetMethod(
            "AddOrMergeHolding",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        bool Invoke(InstitutionalHolding h, HoldingManagerEntry m) =>
            (bool)method.Invoke(null, [map, h, m])!;

        var added = Invoke(
            Holding(100, 1000, 100, 0, 0),
            new HoldingManagerEntry { ManagerName = "A" }
        );
        var merged = Invoke(
            Holding(50, 500, 50, 10, 5),
            new HoldingManagerEntry { ManagerName = "B" }
        );

        added.Should().BeTrue();
        merged.Should().BeFalse();

        var entry = map.Values.Should().ContainSingle().Subject;
        entry.Shares.Should().Be(150);
        entry.Value.Should().Be(1500);
        entry.VotingAuthSole.Should().Be(150);
        entry.VotingAuthShared.Should().Be(10);
        entry.VotingAuthNone.Should().Be(5);
        entry.ManagerEntries.Should().HaveCount(2);
    }
}
