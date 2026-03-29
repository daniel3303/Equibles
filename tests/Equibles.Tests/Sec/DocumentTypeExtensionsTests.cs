using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Extensions;

namespace Equibles.Tests.Sec;

public class DocumentTypeExtensionsTests {
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
    [InlineData("FormFour", DocumentTypeFilter.FormFour)]
    [InlineData("FormThree", DocumentTypeFilter.FormThree)]
    public void ToSecEdgarFilter_MappedType_ReturnsCorrectFilter(string documentTypeValue, DocumentTypeFilter expectedFilter) {
        var docType = DocumentType.FromValue(documentTypeValue);

        var result = docType.ToSecEdgarFilter();

        result.Should().Be(expectedFilter);
    }

    [Fact]
    public void ToSecEdgarFilter_UnmappedType_ReturnsNull() {
        var result = DocumentType.Other.ToSecEdgarFilter();

        result.Should().BeNull();
    }

    [Fact]
    public void ToSecEdgarFilter_CustomUnregisteredType_ReturnsNull() {
        var custom = new DocumentType("CustomFiling", "CUSTOM-99");

        var result = custom.ToSecEdgarFilter();

        result.Should().BeNull();
    }

    [Fact]
    public void ToSecEdgarFilter_AllFilterValues_AreCoveredByMapping() {
        var allFilters = Enum.GetValues<DocumentTypeFilter>();
        var mappedFilters = DocumentType.GetAll()
            .Select(dt => dt.ToSecEdgarFilter())
            .Where(f => f.HasValue)
            .Select(f => f!.Value)
            .ToHashSet();

        mappedFilters.Should().BeEquivalentTo(allFilters,
            "every DocumentTypeFilter value should be reachable from some DocumentType");
    }

    [Theory]
    [InlineData("10-K", DocumentTypeFilter.TenK)]
    [InlineData("10-Q", DocumentTypeFilter.TenQ)]
    [InlineData("8-K", DocumentTypeFilter.EightK)]
    [InlineData("20-F", DocumentTypeFilter.TwentyF)]
    [InlineData("4", DocumentTypeFilter.FormFour)]
    [InlineData("3", DocumentTypeFilter.FormThree)]
    public void FromFormName_ThenToSecEdgarFilter_RoundTrips(string formName, DocumentTypeFilter expectedFilter) {
        var docType = DocumentTypeExtensions.FromFormName(formName);

        var result = docType.ToSecEdgarFilter();

        result.Should().Be(expectedFilter);
    }
}
