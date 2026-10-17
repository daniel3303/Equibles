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
}
