using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

// Record-replay against real Forms 4 whose indirect lines sit in several vehicles. Each line names
// its vehicle by the filed nature of ownership plus the footnotes on the ownershipNature block, so
// a position keeps one closing balance per vehicle instead of one per direct/indirect.
public class InsiderFilingParserOwnershipVehicleTests
{
    private static List<InsiderTransaction> Parse(
        string asset,
        DateOnly filingDate,
        DateOnly period
    )
    {
        var xml = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "InsiderTrading", asset)
        );
        var filing = new FilingData
        {
            AccessionNumber = "0000000000-26-000001",
            Form = "4",
            FilingDate = filingDate,
            ReportDate = period,
        };
        return InsiderFilingParser.ParseTransactions(
            InsiderFilingParser.TryGetOwnershipRoot(xml),
            new InsiderOwner { OwnerCik = "1", Name = "Owner" },
            Guid.NewGuid(),
            filing,
            false
        );
    }

    private static IEnumerable<(string Nature, string Footnotes)> Vehicles(
        IEnumerable<InsiderTransaction> rows
    ) =>
        rows.Where(row =>
                row.OwnershipNature == OwnershipNature.Indirect
                && row.SecurityKind == InsiderSecurityKind.NonDerivative
            )
            .Select(row =>
                (row.NatureOfOwnership, string.Join(",", row.OwnershipNatureFootnoteIds))
            );

    [Fact]
    public void ParseTransactions_NvidiaCeoForm4_RecordsEveryVehicleAsFiled()
    {
        // NVIDIA's CEO, accession 0001197649-26-000012: a gift by his living trust and nine
        // holding lines, one per trust or LLC.
        var rows = Parse(
            "form4-nvidia-ceo-vehicles.xml",
            new DateOnly(2026, 9, 18),
            new DateOnly(2026, 9, 16)
        );

        Vehicles(rows)
            .Should()
            .Equal(
                ("By Trust", "F4"),
                ("By Irrevocable Trust", "F5"),
                ("By Irrevocable Remainder Trust", "F6"),
                ("By Limited Liability Company 1", "F7"),
                ("By Limited Liability Company 2", "F8"),
                ("By Limited Liability Company 3", "F9"),
                ("By Limited Liability Company 4", "F10"),
                ("By Limited Liability Company 5", "F11"),
                ("By Limited Liability Company 6", "F12")
            );
        var direct = rows.Single(row => row.OwnershipNature == OwnershipNature.Direct);
        direct.NatureOfOwnership.Should().BeNull();
        direct.OwnershipNatureFootnoteIds.Should().BeEmpty();
        // The direct balance's own footnote (shares vested from RSUs) is kept apart.
        direct.BalanceFootnoteIds.Should().Equal("F2");
    }

    [Fact]
    public void ParseTransactions_SameNatureText_KeepsEachLinesFootnotes()
    {
        // Dell, accession 0001193125-26-421726: eighteen sales out of one partnership, then four
        // holding lines that all read "See footnote", each pointing to its own entity.
        var rows = Parse(
            "form4-dell-see-footnote-vehicles.xml",
            new DateOnly(2026, 10, 14),
            new DateOnly(2026, 10, 9)
        );

        Vehicles(rows)
            .Skip(18)
            .Should()
            .Equal(
                ("Held through Silver Lake Group, L.L.C.", "F4,F5"),
                ("See footnote", "F6"),
                ("See footnote", "F7"),
                ("See footnote", "F9"),
                ("See footnote", "F27")
            );
    }

    [Fact]
    public void ParseTransactions_FootnoteOnTheReportedBalance_IsRecordedApart()
    {
        // Nelnet, accession 0001008886-26-000109, names each trust and GRAT in a footnote on the
        // shares owned rather than on the nature of ownership.
        var rows = Parse(
            "form4-nelnet-balance-footnoted-holdings.xml",
            new DateOnly(2026, 4, 30),
            new DateOnly(2026, 4, 30)
        );

        rows.Where(row => row.OwnershipNature == OwnershipNature.Indirect)
            .Select(row =>
                (
                    row.NatureOfOwnership,
                    Nature: string.Join(',', row.OwnershipNatureFootnoteIds),
                    Balance: string.Join(',', row.BalanceFootnoteIds)
                )
            )
            .Should()
            .Equal(
                ("By Trust", "", "F1"),
                ("By GRAT", "", "F3"),
                ("By GRAT", "", "F4"),
                ("By Trust", "", "F5"),
                ("By Trust", "", "F6"),
                ("By revocable trust", "", "F7"),
                ("By revocable trust", "", "F8"),
                ("By spouse", "", "")
            );
    }

    [Fact]
    public void ParseTransactions_FootnoteOnDirectOrIndirectOwnership_BelongsToTheVehicle()
    {
        // Eli Lilly's CEO, accession 0001538604-26-000004, attaches its trust footnote to the
        // direct-or-indirect code rather than to the nature of ownership.
        var rows = Parse(
            "form4-lilly-ceo-vehicles.xml",
            new DateOnly(2026, 2, 11),
            new DateOnly(2026, 2, 9)
        );

        Vehicles(rows)
            .Should()
            .Equal(
                ("By David A. Ricks 2025 GRAT", "F1"),
                ("By David A. Ricks 2022 GRAT", ""),
                ("By Trust", "F1"),
                ("By David A. Ricks SLAT", "F1"),
                ("401(k)", "")
            );
        rows.Where(row => row.OwnershipNature == OwnershipNature.Indirect)
            .Sum(row => row.SharesOwnedAfter)
            .Should()
            .Be(137_065);
    }
}
