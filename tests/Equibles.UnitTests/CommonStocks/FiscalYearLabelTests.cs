using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Stored fiscal years are the calendar year a period ends in; a company that names its year
/// for the year it starts in prints one lower. The label moves, the stored year never does, and
/// a year the caller types in the company's naming maps back to the stored one.
/// </summary>
public class FiscalYearLabelTests
{
    private static EquityIssuer Issuer(int? offset)
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "HD",
            Name: "Home Depot"
        );
        issuer.FiscalYearLabelOffset = offset;
        return issuer;
    }

    [Theory]
    [InlineData(2026, null, 2026)]
    [InlineData(2026, 0, 2026)]
    [InlineData(2026, -1, 2025)]
    public void For_PrintsTheCompanysOwnYear(int stored, int? offset, int printed)
    {
        FiscalYearLabel.For(stored, offset).Should().Be(printed);
        FiscalYearLabel.For(stored, Issuer(offset)).Should().Be(printed);
    }

    [Theory]
    [InlineData(2025, null, 2025)]
    [InlineData(2025, 0, 2025)]
    [InlineData(2025, -1, 2026)]
    public void Stored_MapsATypedCompanyYearBackToTheStoredYear(int typed, int? offset, int stored)
    {
        FiscalYearLabel.Stored(typed, offset).Should().Be(stored);
        FiscalYearLabel.Stored(typed, Issuer(offset)).Should().Be(stored);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void StoredAndFor_RoundTrip(int? offset)
    {
        FiscalYearLabel.Stored(FiscalYearLabel.For(2026, offset), offset).Should().Be(2026);
    }

    [Fact]
    public void For_NullIssuer_PrintsTheStoredYear()
    {
        FiscalYearLabel.For(2026, (EquityIssuer)null).Should().Be(2026);
        FiscalYearLabel.Note(null).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Note_StoredYearsArePrinted_SaysNothing(int? offset)
    {
        FiscalYearLabel.Note(Issuer(offset)).Should().BeNull();
    }

    [Fact]
    public void Note_StartYearNaming_SaysSo()
    {
        FiscalYearLabel.Note(Issuer(-1)).Should().Contain("calendar year it starts in");
    }
}
