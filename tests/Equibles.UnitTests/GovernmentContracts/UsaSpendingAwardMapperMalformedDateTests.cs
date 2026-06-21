using Equibles.GovernmentContracts.HostedService.Services;
using Equibles.Integrations.GovernmentContracts.Models;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

/// <summary>
/// Contract: <see cref="UsaSpendingAwardMapper.Map"/> reads the USAspending wire date
/// strings (Start / End / Last Modified) through a strict <c>TryParseExact</c>, so a
/// malformed value must degrade to a null date and leave the rest of the row mappable.
/// A throw here would abort the whole streaming import batch on a single bad cell.
/// </summary>
public class UsaSpendingAwardMapperMalformedDateTests
{
    [Fact]
    public void Map_nulls_an_unparseable_date_and_still_maps_the_row()
    {
        var record = new UsaSpendingAwardRecord
        {
            GeneratedInternalId = "CONT_AWD_ABC123",
            RecipientName = "Lockheed Martin Corporation",
            ContractAwardType = "DEFINITIVE CONTRACT",
            StartDate = "2024-13-45", // impossible month/day — cannot be a valid date
            EndDate = "2027-03-14", // a sibling valid date must remain unaffected
        };

        var entity = UsaSpendingAwardMapper.Map(record, Guid.NewGuid());

        entity.Should().NotBeNull("a malformed date must not abort the import row");
        entity!
            .ActionDate.Should()
            .BeNull("an unparseable Start Date must degrade to null, not throw");
        entity.EndDate.Should().Be(new DateOnly(2027, 3, 14));
        entity.AwardUniqueKey.Should().Be("CONT_AWD_ABC123");
    }
}
