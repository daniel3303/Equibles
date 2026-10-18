using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderBucketTokensTests
{
    [Fact]
    public void ParseEntityTypes_ReadsCommaSeparatedTokensInAnyCase()
    {
        var (types, error) = HolderBucketTokens.ParseEntityTypes(" ADV, bank ,unk,");

        error.Should().BeNull();
        types
            .Should()
            .BeEquivalentTo([
                HolderEntityType.RegisteredAdviser,
                HolderEntityType.Bank,
                HolderEntityType.Unknown,
            ]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData(",")]
    public void ParseEntityTypes_BlankMeansNoFilter(string tokens)
    {
        HolderBucketTokens.ParseEntityTypes(tokens).Should().Be((null, null));
    }

    [Fact]
    public void ParseEntityTypes_UnknownToken_NamesTheAcceptedTokens()
    {
        var (types, error) = HolderBucketTokens.ParseEntityTypes("adv,hedge");

        types.Should().BeNull();
        error
            .Should()
            .Be(
                "Unknown holder type 'hedge'. Use one or more of: adv, era, ia, bank, ins, bd, fund, plan, nonus, unk."
            );
    }

    [Fact]
    public void EveryEntityType_HasOneTokenThatParsesBack()
    {
        foreach (var type in Enum.GetValues<HolderEntityType>())
            HolderBucketTokens
                .ParseEntityTypes(HolderBucketTokens.Token(type))
                .Types.Should()
                .Equal(type);
    }

    [Fact]
    public void Label_ShowsADashForAHolderNotYetProfiled()
    {
        HolderBucketTokens.Label(null).Should().Be("—");
        HolderBucketTokens.Label(HolderEntityType.BrokerDealer).Should().Be("Broker-dealer");
    }

    [Fact]
    public void ParseEntityTypes_ReadsOneTokenPerElementAndCommasInsideOne()
    {
        var (types, error) = HolderBucketTokens.ParseEntityTypes(["BANK", null, "ins, plan", " "]);

        error.Should().BeNull();
        types
            .Should()
            .BeEquivalentTo([
                HolderEntityType.Bank,
                HolderEntityType.InsuranceCompany,
                HolderEntityType.BenefitPlan,
            ]);
    }

    [Fact]
    public void ParseEntityTypes_EmptyOrNullArray_MeansNoFilter()
    {
        HolderBucketTokens.ParseEntityTypes((string[])null).Types.Should().BeNull();
        HolderBucketTokens.ParseEntityTypes(new string[0]).Types.Should().BeNull();
    }

    [Fact]
    public void ParseStyles_ReadsTokensInAnyCaseAcrossElements()
    {
        var (styles, error) = HolderBucketTokens.ParseStyles(["IDX, conc", null, "unk"]);

        error.Should().BeNull();
        styles
            .Should()
            .BeEquivalentTo([
                HolderStyleBand.IndexLike,
                HolderStyleBand.Concentrated,
                HolderStyleBand.Unknown,
            ]);
    }

    [Fact]
    public void ParseStyles_UnknownToken_NamesTheAcceptedTokens()
    {
        var (styles, error) = HolderBucketTokens.ParseStyles(["conc", "growth"]);

        styles.Should().BeNull();
        error
            .Should()
            .Be("Unknown style 'growth'. Use one or more of: idx, sys, conc, divact, unk.");
    }

    [Fact]
    public void ParseStyles_BlankMeansNoFilter()
    {
        HolderBucketTokens.ParseStyles(null).Should().Be((null, null));
        HolderBucketTokens.ParseStyles([" ", ","]).Should().Be((null, null));
    }

    [Fact]
    public void EveryStyleBand_HasOneTokenThatParsesBack()
    {
        foreach (var style in Enum.GetValues<HolderStyleBand>())
            HolderBucketTokens
                .ParseStyles([HolderBucketTokens.StyleToken(style)])
                .Styles.Should()
                .Equal(style);
    }

    [Fact]
    public void StyleLabel_ShowsADashForAHolderNotYetMeasured()
    {
        HolderBucketTokens.StyleLabel(null).Should().Be("—");
        HolderBucketTokens
            .StyleLabel(HolderStyleBand.DiversifiedActive)
            .Should()
            .Be("Diversified active");
        HolderBucketTokens
            .Describe(
                new HashSet<HolderStyleBand> { HolderStyleBand.Unknown, HolderStyleBand.IndexLike }
            )
            .Should()
            .Be("idx, unk");
    }
}
