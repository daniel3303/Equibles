using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Extensions;

namespace Equibles.UnitTests.Holdings;

// The form-type -> FilingType map is the single point that decides which SEC
// submissions the holdings pipeline ingests, so every accepted spelling (daily
// index vs full-text "SC" form, base vs amendment) and the reject path are pinned.
public class HoldingsFilingTypeExtensionsTests
{
    [Theory]
    [InlineData("13F-HR", FilingType.Form13F)]
    [InlineData("13F-HR/A", FilingType.Form13F)]
    [InlineData("SCHEDULE 13D", FilingType.Schedule13D)]
    [InlineData("SCHEDULE 13D/A", FilingType.Schedule13D)]
    [InlineData("SC 13D", FilingType.Schedule13D)]
    [InlineData("SCHEDULE 13G", FilingType.Schedule13G)]
    [InlineData("SCHEDULE 13G/A", FilingType.Schedule13G)]
    [InlineData("SC 13G", FilingType.Schedule13G)]
    [InlineData("schedule 13d", FilingType.Schedule13D)]
    public void ToHoldingsFilingType_KnownForm_MapsToType(string formType, FilingType expected)
    {
        formType.ToHoldingsFilingType().Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("8-K")]
    [InlineData("SCHEDULE 13F")]
    [InlineData("13D")]
    public void ToHoldingsFilingType_UnsupportedOrEmpty_ReturnsNull(string formType)
    {
        formType.ToHoldingsFilingType().Should().BeNull();
    }

    [Theory]
    [InlineData("SCHEDULE 13D/A", true)]
    [InlineData("13F-HR/A", true)]
    [InlineData("SCHEDULE 13D", false)]
    [InlineData("13F-HR", false)]
    [InlineData(null, false)]
    public void IsAmendmentFormType_DetectsAmendmentMarker(string formType, bool expected)
    {
        formType.IsAmendmentFormType().Should().Be(expected);
    }
}
