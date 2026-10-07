using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

// The relationship boxes are per filing: the same owner can be a director at one issuer and
// only a 10% holder at another, so every row carries the boxes of the filing it came from.
// Joint filers share the reported trade, so their boxes are combined.
public class InsiderFilingParserOwnerRelationshipTests
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

    private static XElement Owner(string cik, params string[] boxes) =>
        new(
            "reportingOwner",
            new XElement("reportingOwnerId", new XElement("rptOwnerCik", cik)),
            new XElement("reportingOwnerRelationship", boxes.Select(box => new XElement(box, "1")))
        );

    private static XElement Purchase() =>
        new(
            "nonDerivativeTransaction",
            new XElement("securityTitle", new XElement("value", "Common Stock")),
            new XElement("transactionDate", new XElement("value", "2026-01-02")),
            new XElement("transactionCoding", new XElement("transactionCode", "P")),
            new XElement(
                "transactionAmounts",
                new XElement("transactionShares", new XElement("value", "1000")),
                new XElement("transactionPricePerShare", new XElement("value", "10"))
            )
        );

    [Fact]
    public void ParseTransactions_JointFilingFundAndItsDirector_StampsTheUnionOfBoxes()
    {
        // A fund listed first with the director who manages it: the director's interest in the
        // trade must not depend on filing order, so both filers' boxes reach every row.
        var root = new XElement(
            "ownershipDocument",
            Owner("0000000001", "isTenPercentOwner"),
            Owner("0000000002", "isDirector"),
            new XElement("nonDerivativeTable", Purchase(), Purchase())
        );

        var transactions = ParseAll(root);

        transactions.Should().HaveCount(2);
        transactions
            .Should()
            .OnlyContain(t =>
                t.OwnerRelationship
                == (InsiderRelationship.TenPercentOwner | InsiderRelationship.Director)
            );
    }

    [Fact]
    public void ParseTransactions_DirectorWhoIsAlsoTenPercentOwner_KeepsBothBoxes()
    {
        var root = new XElement(
            "ownershipDocument",
            Owner("0000000001", "isDirector", "isTenPercentOwner"),
            new XElement("nonDerivativeTable", Purchase())
        );

        var transactions = ParseAll(root);

        transactions
            .Should()
            .ContainSingle()
            .Which.OwnerRelationship.Should()
            .Be(InsiderRelationship.Director | InsiderRelationship.TenPercentOwner);
    }

    [Fact]
    public void ParseTransactions_NoBoxTicked_RecordsNoneRatherThanUnknown()
    {
        var root = new XElement(
            "ownershipDocument",
            Owner("0000000001"),
            new XElement("nonDerivativeTable", Purchase())
        );

        ParseAll(root)
            .Should()
            .ContainSingle()
            .Which.OwnerRelationship.Should()
            .Be(InsiderRelationship.None);
    }

    [Fact]
    public void ParseOwnerRelationship_NoRelationshipElement_IsNullSoTheRowStaysUnknown()
    {
        var root = new XElement(
            "ownershipDocument",
            new XElement(
                "reportingOwner",
                new XElement("reportingOwnerId", new XElement("rptOwnerCik", "0000000001"))
            ),
            new XElement("nonDerivativeTable", Purchase())
        );

        InsiderFilingParser.ParseOwnerRelationship(root).Should().BeNull();
    }

    [Fact]
    public void ParseOwnerRelationship_NoReportingOwner_IsNull()
    {
        var root = new XElement(
            "ownershipDocument",
            new XElement("nonDerivativeTable", Purchase())
        );

        InsiderFilingParser.ParseOwnerRelationship(root).Should().BeNull();
    }
}
