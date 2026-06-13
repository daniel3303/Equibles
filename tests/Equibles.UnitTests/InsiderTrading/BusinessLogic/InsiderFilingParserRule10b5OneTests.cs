using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

// The Rule 10b5-1(c) affirmative-defense checkbox the SEC added to Form 4/5 in 2023
// is the document-level <aff10b5One> element. It is a single per-filing flag, so every
// row a filing contributes must carry the same value, and an absent element (pre-2023
// schema) must stay null so "unknown" is distinct from an explicit unchecked box.
public class InsiderFilingParserRule10b5OneTests
{
    private static List<InsiderTransaction> ParseAll(XElement root)
    {
        var owner = new InsiderOwner { OwnerCik = "0000000001", Name = "Owner" };
        var filing = new FilingData
        {
            AccessionNumber = "0000000000-26-000001",
            Form = "4",
            FilingDate = new DateOnly(2026, 1, 5),
            ReportDate = new DateOnly(2026, 1, 2),
        };
        return InsiderFilingParser.ParseTransactions(root, owner, Guid.NewGuid(), filing, false);
    }

    private static XElement Transaction(string code) =>
        new(
            "nonDerivativeTransaction",
            new XElement("securityTitle", new XElement("value", "Common Stock")),
            new XElement("transactionDate", new XElement("value", "2026-01-02")),
            new XElement("transactionCoding", new XElement("transactionCode", code)),
            new XElement(
                "transactionAmounts",
                new XElement("transactionShares", new XElement("value", "1000")),
                new XElement("transactionPricePerShare", new XElement("value", "10"))
            )
        );

    [Fact]
    public void ParseTransactions_RealFilingWithBoxChecked_FlagsEveryRowTrue()
    {
        // A real Form 4 (Dutch Bros, accession 0001866581-25-000077) with the Rule
        // 10b5-1 box checked and 14 plan sales — locks the element name and position
        // against the live ownership schema, not a hand-built approximation.
        var xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "InsiderTrading",
                "form4-rule10b5one-checked.xml"
            )
        );
        var root = InsiderFilingParser.TryGetOwnershipRoot(xml);

        var transactions = ParseAll(root);

        transactions.Should().HaveCount(14);
        transactions.Should().OnlyContain(t => t.IsRule10b5One == true);
    }

    [Fact]
    public void ParseTransactions_BoxPresentUnchecked_FlagsRowsFalse()
    {
        var root = new XElement(
            "ownershipDocument",
            new XElement("nonDerivativeTable", Transaction("S")),
            new XElement("aff10b5One", "0")
        );

        var transactions = ParseAll(root);

        transactions.Should().ContainSingle();
        transactions[0].IsRule10b5One.Should().BeFalse();
    }

    [Fact]
    public void ParseTransactions_BoxAbsent_FlagIsNull()
    {
        var root = new XElement(
            "ownershipDocument",
            new XElement("nonDerivativeTable", Transaction("P"))
        );

        var transactions = ParseAll(root);

        transactions.Should().ContainSingle();
        transactions[0].IsRule10b5One.Should().BeNull();
    }

    [Fact]
    public void ParseTransactions_BoxChecked_AppliesToEveryRowInTheFiling()
    {
        var root = new XElement(
            "ownershipDocument",
            new XElement("nonDerivativeTable", Transaction("S"), Transaction("S")),
            new XElement("aff10b5One", "1")
        );

        var transactions = ParseAll(root);

        transactions.Should().HaveCount(2);
        transactions.Should().OnlyContain(t => t.IsRule10b5One == true);
    }
}
