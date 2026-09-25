using Equibles.Sec.FinancialFacts.Data.Registrations;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FtdImportServiceSiblingCusipTests
{
    private static readonly Guid IssuerId = Guid.NewGuid();

    private static readonly (string Symbol, string Accession)[] CoverPage =
    [
        ("LBRDK", "0001140361-26-033932"),
        ("LBRDA", "0001140361-26-033932"),
    ];

    [Fact]
    public void RegisteredTogether_SymbolsOnOneCoverPage_MatchAcrossSeparatorSpellings()
    {
        CoverPageRegistration
            .RegisteredTogether([("BFA", "a-1"), ("BFB", "a-1")], "BF-A", "BF.B")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void RegisteredTogether_SymbolsOnDifferentFilings_AreNotProof()
    {
        CoverPageRegistration
            .RegisteredTogether([("OLD", "a-1"), ("NEW", "a-2")], "OLD", "NEW")
            .Should()
            .BeFalse();
    }

    [Fact]
    public void RegisteredTogether_MissingAccession_IsNotProof()
    {
        CoverPageRegistration
            .RegisteredTogether([("A", null), ("B", null)], "A", "B")
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ResolveRetiredSiblingTicker_SoleStagedCandidate_ReturnsSibling()
    {
        FtdImportService
            .ResolveRetiredSiblingTicker(
                IssuerId,
                "LBRDK",
                "530307107",
                [new(IssuerId, "LBRDA", null, ["530307107"])],
                CoverPage
            )
            .Should()
            .Be("LBRDA");
    }

    [Fact]
    public void ResolveRetiredSiblingTicker_SeededCusip_ReturnsSibling()
    {
        FtdImportService
            .ResolveRetiredSiblingTicker(
                IssuerId,
                "LBRDK",
                "530307107",
                [new(IssuerId, "LBRDA", "530307107", [])],
                CoverPage
            )
            .Should()
            .Be("LBRDA");
    }

    [Fact]
    public void ResolveRetiredSiblingTicker_EqualDateConflict_Abstains()
    {
        FtdImportService
            .ResolveRetiredSiblingTicker(
                IssuerId,
                "LBRDK",
                "530307107",
                [new(IssuerId, "LBRDA", null, ["530307107", "530307999"])],
                CoverPage
            )
            .Should()
            .BeNull();
    }

    [Fact]
    public void ResolveRetiredSiblingTicker_SeededOtherCusip_IgnoresStaleCandidate()
    {
        FtdImportService
            .ResolveRetiredSiblingTicker(
                IssuerId,
                "LBRDK",
                "530307107",
                [new(IssuerId, "LBRDA", "530307999", ["530307107"])],
                CoverPage
            )
            .Should()
            .BeNull();
    }

    [Fact]
    public void ResolveRetiredSiblingTicker_TwoListingsStateCusip_Abstains()
    {
        FtdImportService
            .ResolveRetiredSiblingTicker(
                IssuerId,
                "LBRDK",
                "530307107",
                [
                    new(IssuerId, "LBRDA", null, ["530307107"]),
                    new(Guid.NewGuid(), "LBRDA", null, ["530307107"]),
                ],
                CoverPage
            )
            .Should()
            .BeNull();
    }

    [Fact]
    public void ResolveRetiredSiblingTicker_AnotherIssuersListing_Abstains()
    {
        FtdImportService
            .ResolveRetiredSiblingTicker(
                IssuerId,
                "LBRDK",
                "530307107",
                [new(Guid.NewGuid(), "LBRDA", null, ["530307107"])],
                CoverPage
            )
            .Should()
            .BeNull();
    }

    [Fact]
    public void ResolveRetiredSiblingTicker_PresentationsOwnRetiredListing_Abstains()
    {
        FtdImportService
            .ResolveRetiredSiblingTicker(
                IssuerId,
                "LBRDK",
                "530307107",
                [new(IssuerId, "LBRDK", null, ["530307107"])],
                CoverPage
            )
            .Should()
            .BeNull();
    }

    [Fact]
    public void ResolveRetiredSiblingTicker_NotCoRegistered_Abstains()
    {
        FtdImportService
            .ResolveRetiredSiblingTicker(
                IssuerId,
                "LBRDK",
                "530307107",
                [new(IssuerId, "LBRDA", null, ["530307107"])],
                [("LBRDK", "0001140361-26-033932")]
            )
            .Should()
            .BeNull();
    }

    private static readonly Guid Issuer = Guid.NewGuid();

    private static FtdImportService.SiblingSecurity Security(
        string ticker,
        string cusip,
        DateOnly? delistedOn = null
    ) =>
        new(
            Guid.NewGuid(),
            Issuer,
            cusip,
            [new FtdImportService.SiblingListing(ticker, delistedOn)]
        );

    private static FtdRecord Fail(string date, string cusip, string symbol) =>
        new()
        {
            SettlementDate = DateOnly.ParseExact(date, "yyyyMMdd"),
            Cusip = cusip,
            Symbol = symbol,
        };

    private static readonly Dictionary<Guid, List<(string Symbol, string Accession)>> UnitCover =
        new() { [Issuer] = [("AMAC", "a-1"), ("AMACU", "a-1")] };

    [Fact]
    public void PlanSiblingCusipMoves_HeldCusipTradesAsCoRegisteredSibling_MovesIt()
    {
        var moves = FtdImportService.PlanSiblingCusipMoves(
            [Fail("20260728", "G03456129", "AMACU")],
            [Security("AMAC", "G03456129"), Security("AMACU", null)],
            UnitCover
        );

        moves
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(new FtdImportService.SiblingCusipMove(Issuer, "G03456129", "AMAC", "AMACU"));
    }

    [Fact]
    public void PlanSiblingCusipMoves_HolderEverSeenUnderTheCusip_Abstains()
    {
        FtdImportService
            .PlanSiblingCusipMoves(
                [Fail("20260701", "G03456129", "AMAC"), Fail("20260728", "G03456129", "AMACU")],
                [Security("AMAC", "G03456129"), Security("AMACU", null)],
                UnitCover
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void PlanSiblingCusipMoves_TwoSymbolsOnTheLatestDate_Abstains()
    {
        FtdImportService
            .PlanSiblingCusipMoves(
                [Fail("20260728", "G03456129", "AMACU"), Fail("20260728", "G03456129", "AMACW")],
                [Security("AMAC", "G03456129"), Security("AMACU", null)],
                UnitCover
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void PlanSiblingCusipMoves_SiblingAlreadyHoldsACusip_Abstains()
    {
        FtdImportService
            .PlanSiblingCusipMoves(
                [Fail("20260728", "G03456129", "AMACU")],
                [Security("AMAC", "G03456129"), Security("AMACU", "G03456111")],
                UnitCover
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void PlanSiblingCusipMoves_NotCoRegistered_Abstains()
    {
        FtdImportService
            .PlanSiblingCusipMoves(
                [Fail("20260728", "G03456129", "AMACU")],
                [Security("AMAC", "G03456129"), Security("AMACU", null)],
                new Dictionary<Guid, List<(string Symbol, string Accession)>>
                {
                    [Issuer] = [("AMAC", "a-2"), ("AMACU", "a-1")],
                }
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void PlanSiblingCusipMoves_RetiredSiblingSeenAfterItsDelisting_Abstains()
    {
        FtdImportService
            .PlanSiblingCusipMoves(
                [Fail("20260728", "G03456129", "AMACU")],
                [Security("AMAC", "G03456129"), Security("AMACU", null, new DateOnly(2026, 7, 1))],
                UnitCover
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void PlanSiblingCusipMoves_SeparatorStrippedFailsSymbol_MatchesTheListing()
    {
        var moves = FtdImportService.PlanSiblingCusipMoves(
            [Fail("20260728", "115637100", "BFA")],
            [Security("BF-B", "115637100"), Security("BF-A", null)],
            new Dictionary<Guid, List<(string Symbol, string Accession)>>
            {
                [Issuer] = [("BFB", "a-1"), ("BFA", "a-1")],
            }
        );

        moves.Should().ContainSingle().Which.SiblingTicker.Should().Be("BF-A");
    }
}
