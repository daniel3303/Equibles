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
        FtdImportService
            .RegisteredTogether([("BFA", "a-1"), ("BFB", "a-1")], "BF-A", "BF.B")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void RegisteredTogether_SymbolsOnDifferentFilings_AreNotProof()
    {
        FtdImportService
            .RegisteredTogether([("OLD", "a-1"), ("NEW", "a-2")], "OLD", "NEW")
            .Should()
            .BeFalse();
    }

    [Fact]
    public void RegisteredTogether_MissingAccession_IsNotProof()
    {
        FtdImportService
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
}
