using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Extensions;

namespace Equibles.UnitTests.Sec;

public class DocumentTypeExtensionsTests
{
    [Theory]
    [InlineData("TenK", DocumentTypeFilter.TenK)]
    [InlineData("TenQ", DocumentTypeFilter.TenQ)]
    [InlineData("TenKa", DocumentTypeFilter.TenKa)]
    [InlineData("TenQa", DocumentTypeFilter.TenQa)]
    [InlineData("EightK", DocumentTypeFilter.EightK)]
    [InlineData("EightKa", DocumentTypeFilter.EightKa)]
    [InlineData("TwentyF", DocumentTypeFilter.TwentyF)]
    [InlineData("SixK", DocumentTypeFilter.SixK)]
    [InlineData("FortyF", DocumentTypeFilter.FortyF)]
    [InlineData("TwentyFa", DocumentTypeFilter.TwentyFa)]
    [InlineData("SixKa", DocumentTypeFilter.SixKa)]
    [InlineData("FortyFa", DocumentTypeFilter.FortyFa)]
    [InlineData("FormFour", DocumentTypeFilter.FormFour)]
    [InlineData("FormThree", DocumentTypeFilter.FormThree)]
    [InlineData("FormFive", DocumentTypeFilter.FormFive)]
    [InlineData("FormFourA", DocumentTypeFilter.FormFourA)]
    [InlineData("FormThreeA", DocumentTypeFilter.FormThreeA)]
    [InlineData("FormFiveA", DocumentTypeFilter.FormFiveA)]
    [InlineData("Form144", DocumentTypeFilter.Form144)]
    [InlineData("FormD", DocumentTypeFilter.FormD)]
    [InlineData("FormDa", DocumentTypeFilter.FormDa)]
    [InlineData("NCen", DocumentTypeFilter.NCen)]
    [InlineData("NCenA", DocumentTypeFilter.NCenA)]
    [InlineData("NportP", DocumentTypeFilter.NportP)]
    [InlineData("NportPa", DocumentTypeFilter.NportPa)]
    [InlineData("Def14A", DocumentTypeFilter.Def14A)]
    public void ToSecEdgarFilter_MappedType_ReturnsCorrectFilter(
        string documentTypeValue,
        DocumentTypeFilter expectedFilter
    )
    {
        var docType = DocumentType.FromValue(documentTypeValue);

        var result = docType.ToSecEdgarFilter();

        result.Should().Be(expectedFilter);
    }

    [Fact]
    public void ToSecEdgarFilter_UnmappedType_ReturnsNull()
    {
        var result = DocumentType.Other.ToSecEdgarFilter();

        result.Should().BeNull();
    }

    [Fact]
    public void ToSecEdgarFilter_CustomUnregisteredType_ReturnsNull()
    {
        var custom = new DocumentType("CustomFiling", "CUSTOM-99");

        var result = custom.ToSecEdgarFilter();

        result.Should().BeNull();
    }

    [Fact]
    public void ToSecEdgarFilter_AllFilterValues_AreCoveredByMapping()
    {
        var allFilters = Enum.GetValues<DocumentTypeFilter>();
        var mappedFilters = DocumentType
            .GetAll()
            .Select(dt => dt.ToSecEdgarFilter())
            .Where(f => f.HasValue)
            .Select(f => f!.Value)
            .ToHashSet();

        mappedFilters
            .Should()
            .BeEquivalentTo(
                allFilters,
                "every DocumentTypeFilter value should be reachable from some DocumentType"
            );
    }

    [Theory]
    [InlineData("10-K", DocumentTypeFilter.TenK)]
    [InlineData("10-Q", DocumentTypeFilter.TenQ)]
    [InlineData("8-K", DocumentTypeFilter.EightK)]
    [InlineData("20-F", DocumentTypeFilter.TwentyF)]
    [InlineData("6-K", DocumentTypeFilter.SixK)]
    [InlineData("40-F", DocumentTypeFilter.FortyF)]
    [InlineData("20-F/A", DocumentTypeFilter.TwentyFa)]
    [InlineData("6-K/A", DocumentTypeFilter.SixKa)]
    [InlineData("40-F/A", DocumentTypeFilter.FortyFa)]
    [InlineData("4", DocumentTypeFilter.FormFour)]
    [InlineData("3", DocumentTypeFilter.FormThree)]
    [InlineData("5", DocumentTypeFilter.FormFive)]
    [InlineData("4/A", DocumentTypeFilter.FormFourA)]
    [InlineData("3/A", DocumentTypeFilter.FormThreeA)]
    [InlineData("5/A", DocumentTypeFilter.FormFiveA)]
    [InlineData("144", DocumentTypeFilter.Form144)]
    [InlineData("D", DocumentTypeFilter.FormD)]
    [InlineData("D/A", DocumentTypeFilter.FormDa)]
    [InlineData("N-CEN", DocumentTypeFilter.NCen)]
    [InlineData("N-CEN/A", DocumentTypeFilter.NCenA)]
    [InlineData("NPORT-P", DocumentTypeFilter.NportP)]
    [InlineData("NPORT-P/A", DocumentTypeFilter.NportPa)]
    public void FromFormName_ThenToSecEdgarFilter_RoundTrips(
        string formName,
        DocumentTypeFilter expectedFilter
    )
    {
        var docType = DocumentTypeExtensions.FromFormName(formName);

        var result = docType.ToSecEdgarFilter();

        result.Should().Be(expectedFilter);
    }
}
