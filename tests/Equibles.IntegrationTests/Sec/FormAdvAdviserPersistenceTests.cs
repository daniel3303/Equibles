using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins that the Form ADV adviser entity (PR slice 1) maps and round-trips through the
/// real ParadeDB schema — proving the AddFormAdv migration applies and that the column set,
/// nullable regulatory-AUM figures, and fee-structure flags persist and read back intact.
/// Querying by the Organization CRD number also exercises its unique index.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FormAdvAdviserPersistenceTests : ParadeDbMcpTestBase
{
    public FormAdvAdviserPersistenceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task FormAdvAdviser_RoundTrips_ThroughTheParadeDbSchema()
    {
        var adviser = new FormAdvAdviser
        {
            Id = Guid.NewGuid(),
            Crd = 231,
            SecNumber = "801-54739",
            LegalName = "BNY MELLON SECURITIES CORPORATION",
            PrimaryBusinessName = "BNY MELLON SECURITIES CORPORATION",
            MainOfficeCity = "NEW YORK",
            MainOfficeState = "NY",
            MainOfficeCountry = "United States",
            WebsiteAddress = "HTTP://WWW.BNYMELLONIM.COM/US",
            SecStatus = "Approved",
            NumberOfEmployees = 333,
            DiscretionaryAum = 829_845_109L,
            NonDiscretionaryAum = 1_651_522_723L,
            TotalRegulatoryAum = 2_481_367_832L,
            ChargesPercentageOfAum = true,
            ChargesPerformanceBased = false,
            ReportDate = new DateOnly(2022, 4, 1),
        };

        DbContext.Set<FormAdvAdviser>().Add(adviser);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var loaded = await DbContext.Set<FormAdvAdviser>().SingleAsync(a => a.Crd == 231);

        loaded.SecNumber.Should().Be("801-54739");
        loaded.LegalName.Should().Be("BNY MELLON SECURITIES CORPORATION");
        loaded.MainOfficeState.Should().Be("NY");
        loaded.NumberOfEmployees.Should().Be(333);
        loaded.TotalRegulatoryAum.Should().Be(2_481_367_832L);
        loaded.DiscretionaryAum.Should().Be(829_845_109L);
        loaded.NonDiscretionaryAum.Should().Be(1_651_522_723L);
        loaded.ChargesPercentageOfAum.Should().BeTrue();
        loaded.ChargesPerformanceBased.Should().BeFalse();
        loaded.ReportDate.Should().Be(new DateOnly(2022, 4, 1));
    }
}
