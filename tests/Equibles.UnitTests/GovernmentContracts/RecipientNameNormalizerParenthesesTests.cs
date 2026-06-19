using Equibles.GovernmentContracts.HostedService.Services;

namespace Equibles.UnitTests.GovernmentContracts;

/// <summary>
/// Pins <see cref="RecipientNameNormalizer.Normalize"/>'s handling of drop-class punctuation
/// (parentheses) — a branch the existing suite never exercises, yet real USAspending recipient
/// names carry parenthetical DBA/division decorations. The contract strips punctuation and the
/// trailing legal suffix, keeping the inner word, so the normalized key stays comparable.
/// </summary>
public class RecipientNameNormalizerParenthesesTests
{
    [Fact]
    public void Normalize_DropsParentheses_KeepsInnerWord_AndStripsTrailingSuffix()
    {
        var key = RecipientNameNormalizer.Normalize("Booz Allen Hamilton (Holding) Corporation");

        // Parentheses removed as punctuation, "HOLDING" kept as a real token, "CORPORATION"
        // stripped as a trailing legal suffix.
        key.Should().Be("BOOZ ALLEN HAMILTON HOLDING");
    }
}
