using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Equibles.Core.Extensions;
using Equibles.Holdings.Data.Models;

namespace Equibles.Tests.Models;

public class HoldingsEnumTests {
    [Theory]
    [InlineData(ShareType.Shares, "Shares")]
    [InlineData(ShareType.Principal, "Principal")]
    public void ShareType_NameForHumans_ReturnsDisplayName(ShareType value, string expected) {
        value.NameForHumans().Should().Be(expected);
    }

    [Theory]
    [InlineData(OptionType.Put, "Put")]
    [InlineData(OptionType.Call, "Call")]
    public void OptionType_NameForHumans_ReturnsDisplayName(OptionType value, string expected) {
        value.NameForHumans().Should().Be(expected);
    }

    [Theory]
    [InlineData(InvestmentDiscretion.Sole, "Sole")]
    [InlineData(InvestmentDiscretion.Defined, "Defined")]
    [InlineData(InvestmentDiscretion.Other, "Other")]
    public void InvestmentDiscretion_NameForHumans_ReturnsDisplayName(InvestmentDiscretion value, string expected) {
        value.NameForHumans().Should().Be(expected);
    }

    [Fact]
    public void ShareType_AllValues_HaveDisplayAttribute() {
        var values = Enum.GetValues<ShareType>();
        values.Should().HaveCount(2);

        foreach (var value in values) {
            var member = typeof(ShareType).GetMember(value.ToString()).First();
            member.GetCustomAttribute<DisplayAttribute>().Should().NotBeNull(
                because: $"{value} should have a Display attribute");
        }
    }

    [Fact]
    public void OptionType_AllValues_HaveDisplayAttribute() {
        var values = Enum.GetValues<OptionType>();
        values.Should().HaveCount(2);

        foreach (var value in values) {
            var member = typeof(OptionType).GetMember(value.ToString()).First();
            member.GetCustomAttribute<DisplayAttribute>().Should().NotBeNull(
                because: $"{value} should have a Display attribute");
        }
    }

    [Fact]
    public void InvestmentDiscretion_AllValues_HaveDisplayAttribute() {
        var values = Enum.GetValues<InvestmentDiscretion>();
        values.Should().HaveCount(3);

        foreach (var value in values) {
            var member = typeof(InvestmentDiscretion).GetMember(value.ToString()).First();
            member.GetCustomAttribute<DisplayAttribute>().Should().NotBeNull(
                because: $"{value} should have a Display attribute");
        }
    }
}
