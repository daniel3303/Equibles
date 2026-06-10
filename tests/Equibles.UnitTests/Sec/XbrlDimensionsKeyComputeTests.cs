using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// DimensionsKey is the unique-index discriminator separating dimensional
/// facts from their consolidated sibling, so its contract is load-bearing in
/// three ways: the consolidated context must map to the empty string (the
/// value every API-sourced row carries), the same dimension set must produce
/// the same key regardless of declaration order (filings reorder members
/// between quarters), and different dimension sets must not collide.
/// </summary>
public class XbrlDimensionsKeyComputeTests
{
    [Fact]
    public void Compute_NoDimensions_ReturnsEmptyString()
    {
        XbrlDimensionsKey.Compute([]).Should().BeEmpty();
        XbrlDimensionsKey.Compute(null).Should().BeEmpty();
    }

    [Fact]
    public void Compute_SameDimensionsDifferentOrder_ProducesSameKey()
    {
        var product = new ParsedXbrlDimension
        {
            Axis = "srt:ProductOrServiceAxis",
            Member = "aapl:IPhoneMember",
        };
        var geography = new ParsedXbrlDimension
        {
            Axis = "srt:StatementGeographicalAxis",
            Member = "srt:AmericasMember",
        };

        var keyOneWay = XbrlDimensionsKey.Compute([product, geography]);
        var keyOtherWay = XbrlDimensionsKey.Compute([geography, product]);

        keyOneWay.Should().Be(keyOtherWay);
        // Lowercase hex SHA-256 — fits the 64-char column and stays index-friendly.
        keyOneWay.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Compute_DifferentMember_ProducesDifferentKey()
    {
        var iphone = new ParsedXbrlDimension
        {
            Axis = "srt:ProductOrServiceAxis",
            Member = "aapl:IPhoneMember",
        };
        var mac = new ParsedXbrlDimension
        {
            Axis = "srt:ProductOrServiceAxis",
            Member = "aapl:MacMember",
        };

        XbrlDimensionsKey.Compute([iphone]).Should().NotBe(XbrlDimensionsKey.Compute([mac]));
    }
}
