using Equibles.Sec.BusinessLogic.Search;

namespace Equibles.UnitTests.Sec;

public class SecFilingItemTitlesTests
{
    public static TheoryData<string> FormEightKItems =>
        new()
        {
            "1.01",
            "1.02",
            "1.03",
            "1.04",
            "1.05",
            "2.01",
            "2.02",
            "2.03",
            "2.04",
            "2.05",
            "2.06",
            "3.01",
            "3.02",
            "3.03",
            "4.01",
            "4.02",
            "5.01",
            "5.02",
            "5.03",
            "5.04",
            "5.05",
            "5.06",
            "5.07",
            "5.08",
            "6.01",
            "6.02",
            "6.03",
            "6.04",
            "6.05",
            "6.06",
            "6.10",
            "7.01",
            "8.01",
            "9.01",
        };

    [Theory]
    [MemberData(nameof(FormEightKItems))]
    public void Label_EveryFormEightKItem_HasALabel(string item)
    {
        SecFilingItemTitles.Label(item).Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("2.07")]
    [InlineData("10.01")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Label_NumberOutsideTheItemList_IsNull(string item)
    {
        SecFilingItemTitles.Label(item).Should().BeNull();
    }

    [Fact]
    public void Describe_LeavesExhibitsOutWhenAnotherItemIsPresent()
    {
        SecFilingItemTitles
            .Describe("2.02,9.01")
            .Should()
            .Be("Results of operations and financial condition (Item 2.02)");
    }

    [Fact]
    public void Describe_ExhibitsAloneAreNamed()
    {
        SecFilingItemTitles
            .Describe("9.01")
            .Should()
            .Be("Financial statements and exhibits (Item 9.01)");
    }

    [Fact]
    public void Describe_NamesAtMostThreeItemsInFilingOrder()
    {
        SecFilingItemTitles
            .Describe("1.01,2.03,5.02,7.01,9.01")
            .Should()
            .Be(
                "Entry into a material definitive agreement (Item 1.01); New direct financial obligation (Item 2.03); Director or officer departure, election or appointment (Item 5.02)"
            );
    }

    [Fact]
    public void Describe_SkipsUnknownAndDuplicateNumbers()
    {
        SecFilingItemTitles.Describe("2.07, 8.01 ,8.01").Should().Be("Other events (Item 8.01)");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2.07")]
    public void Describe_NothingNamed_IsNull(string items)
    {
        SecFilingItemTitles.Describe(items).Should().BeNull();
    }
}
