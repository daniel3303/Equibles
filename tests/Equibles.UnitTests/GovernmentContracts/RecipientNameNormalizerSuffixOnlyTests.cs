using Equibles.GovernmentContracts.HostedService.Services;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

/// <summary>
/// Adversarial cover for <see cref="RecipientNameNormalizer.Normalize"/> on a suffix-only name
/// at or above the minimum key length. The contract strips trailing legal-entity suffixes and
/// returns null "when nothing meaningful remains" — the existing suite pins that for the short
/// suffixes "Inc"/"Co", but those return null on the 4-char length floor, not on suffix stripping.
/// A pure legal suffix that is itself >= 4 chars exercises the stripping promise directly.
/// </summary>
public class RecipientNameNormalizerSuffixOnlyTests
{
    [Fact(Skip = "GH-3845 — suffix-only name >= min length leaks a generic key instead of null")]
    public void Normalize_SuffixOnlyNameAtOrAboveMinLength_ReturnsNull()
    {
        // "Corporation" is a pure legal-entity suffix (no real name token); the contract strips
        // it and returns null, exactly as "Inc"/"Co" do. Nothing meaningful remains to key on.
        RecipientNameNormalizer.Normalize("Corporation").Should().BeNull();
    }
}
