using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserNoSecuritiesOwnedSentinelTests
{
    // Recorded payload from SEC accession 0001466258-23-000203 — a Form 3 for
    // Trane Technologies plc (TT) where the reporting owner holds none of the
    // issuer's securities: <noSecuritiesOwned>1</noSecuritiesOwned> with empty
    // ownership tables. This is the exact shape that re-parsed to zero rows during
    // the reprocess sweep (thousands of "has 1 stored rows but re-parsed 0"
    // warnings), because the parser dropped these while the ingest pipeline stored
    // a 0-shares sentinel for them.
    private const string NoSecuritiesOwnedForm3 = """
        <?xml version="1.0"?>
        <ownershipDocument>
            <schemaVersion>X0206</schemaVersion>
            <documentType>3</documentType>
            <periodOfReport>2023-10-03</periodOfReport>
            <noSecuritiesOwned>1</noSecuritiesOwned>
            <issuer>
                <issuerCik>0001466258</issuerCik>
                <issuerName>Trane Technologies plc</issuerName>
                <issuerTradingSymbol>TT</issuerTradingSymbol>
            </issuer>
            <reportingOwner>
                <reportingOwnerId>
                    <rptOwnerCik>0001996403</rptOwnerCik>
                    <rptOwnerName>de Jesus Assis Ana Paula</rptOwnerName>
                </reportingOwnerId>
                <reportingOwnerRelationship>
                    <isDirector>1</isDirector>
                </reportingOwnerRelationship>
            </reportingOwner>
            <nonDerivativeTable></nonDerivativeTable>
            <derivativeTable></derivativeTable>
            <footnotes></footnotes>
        </ownershipDocument>
        """;

    [Fact]
    public void ParseTransactions_NoSecuritiesOwnedForm3_ProducesSingleZeroSharesSentinel()
    {
        // Contract: a Form 3 declaring noSecuritiesOwned is a real, empty-table filing.
        // The parser is the single source of truth shared by the ingest and reprocess
        // pipelines, so it must reproduce the 0-shares "No Securities Owned" sentinel
        // the ingest pipeline persists — not silently drop the filing to zero rows.
        var root = InsiderFilingParser.TryGetOwnershipRoot(NoSecuritiesOwnedForm3);
        root.Should().NotBeNull();

        var filing = new FilingData
        {
            AccessionNumber = "0001466258-23-000203",
            FilingDate = new DateOnly(2023, 10, 5),
            ReportDate = new DateOnly(2023, 10, 3),
        };

        var result = InsiderFilingParser.ParseTransactions(
            root,
            new InsiderOwner { Id = Guid.NewGuid() },
            Guid.NewGuid(),
            filing,
            isAmendment: false
        );

        var sentinel = result.Should().ContainSingle().Subject;
        sentinel.SecurityTitle.Should().Be("No Securities Owned");
        sentinel.Shares.Should().Be(0);
        sentinel.SharesOwnedAfter.Should().Be(0);
        sentinel.PricePerShare.Should().Be(0);
        sentinel.TransactionOrder.Should().Be(0);
        sentinel.TransactionCode.Should().Be(TransactionCode.Other);
        sentinel.TransactionDate.Should().Be(filing.ReportDate);
        sentinel.SecurityKind.Should().Be(InsiderSecurityKind.Unknown);
        sentinel.IsPriceValid.Should().BeTrue();
    }

    [Fact]
    public void ParseTransactions_EmptyTablesWithoutNoSecuritiesOwned_ProducesNoRows()
    {
        // Guard the gate: an empty-table filing that does NOT declare noSecuritiesOwned
        // (e.g. a Form 4 the parser couldn't read) must not get a bogus sentinel — that
        // would mask a genuine parse gap. Such filings still re-parse to zero.
        var root = XElement.Parse(
            """
            <ownershipDocument>
                <documentType>4</documentType>
                <nonDerivativeTable></nonDerivativeTable>
                <derivativeTable></derivativeTable>
            </ownershipDocument>
            """
        );

        var filing = new FilingData
        {
            AccessionNumber = "0000000000-24-000001",
            FilingDate = new DateOnly(2024, 2, 1),
            ReportDate = new DateOnly(2024, 1, 31),
        };

        var result = InsiderFilingParser.ParseTransactions(
            root,
            new InsiderOwner { Id = Guid.NewGuid() },
            Guid.NewGuid(),
            filing,
            isAmendment: false
        );

        result.Should().BeEmpty();
    }
}
