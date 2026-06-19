using Equibles.GovernmentContracts.Data.Models;
using Equibles.GovernmentContracts.HostedService.Services;
using Equibles.Integrations.GovernmentContracts.Models;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

public class UsaSpendingAwardMapperTests
{
    private static UsaSpendingAwardRecord SampleRecord() =>
        new()
        {
            GeneratedInternalId = "CONT_AWD_ABC123",
            AwardId = "FA8675309",
            RecipientName = "Lockheed Martin Corporation",
            RecipientId = "abc-hash-C",
            Amount = 1_234_567.89m,
            TotalOutlays = 500_000m,
            AwardingAgency = "Department of Defense",
            ContractAwardType = "DEFINITIVE CONTRACT",
            StartDate = "2024-03-15",
            EndDate = "2027-03-14",
            LastModifiedDate = "2024-04-01",
            Naics = "336411",
            Psc = "1510",
            Description = "AIRCRAFT COMPONENTS",
        };

    [Fact]
    public void Map_projects_all_fields_for_a_resolved_company()
    {
        var stockId = Guid.NewGuid();

        var entity = UsaSpendingAwardMapper.Map(SampleRecord(), stockId);

        entity.Should().NotBeNull();
        entity.CommonStockId.Should().Be(stockId);
        entity.AwardUniqueKey.Should().Be("CONT_AWD_ABC123");
        entity.AwardId.Should().Be("FA8675309");
        entity.RecipientName.Should().Be("Lockheed Martin Corporation");
        entity.AwardType.Should().Be(GovernmentContractAwardType.DefinitiveContract);
        entity.Amount.Should().Be(1_234_567.89m);
        entity.TotalOutlays.Should().Be(500_000m);
        entity.ActionDate.Should().Be(new DateOnly(2024, 3, 15));
        entity.EndDate.Should().Be(new DateOnly(2027, 3, 14));
        entity.LastModifiedDate.Should().Be(new DateOnly(2024, 4, 1));
        entity.NaicsCode.Should().Be("336411");
        entity.PscCode.Should().Be("1510");
        entity.Description.Should().Be("AIRCRAFT COMPONENTS");
    }

    [Fact]
    public void Map_falls_back_to_award_id_when_generated_id_is_missing()
    {
        var record = SampleRecord();
        record.GeneratedInternalId = null;

        var entity = UsaSpendingAwardMapper.Map(record, Guid.NewGuid());

        entity.Should().NotBeNull();
        entity.AwardUniqueKey.Should().Be("FA8675309");
    }

    [Fact]
    public void Map_returns_null_when_no_stable_identifier_exists()
    {
        var record = SampleRecord();
        record.GeneratedInternalId = null;
        record.AwardId = null;

        UsaSpendingAwardMapper.Map(record, Guid.NewGuid()).Should().BeNull();
    }

    [Theory]
    [InlineData("BPA CALL", GovernmentContractAwardType.BpaCall)]
    [InlineData("Purchase Order", GovernmentContractAwardType.PurchaseOrder)]
    [InlineData("delivery order", GovernmentContractAwardType.DeliveryOrder)]
    [InlineData("DEFINITIVE CONTRACT", GovernmentContractAwardType.DefinitiveContract)]
    [InlineData("Cooperative Agreement", GovernmentContractAwardType.Unknown)]
    [InlineData(null, GovernmentContractAwardType.Unknown)]
    public void MapAwardType_maps_known_labels_case_insensitively(
        string label,
        GovernmentContractAwardType expected
    )
    {
        UsaSpendingAwardMapper.MapAwardType(label).Should().Be(expected);
    }

    [Fact]
    public void Map_keeps_only_the_leading_token_of_a_decorated_naics_value()
    {
        var record = SampleRecord();
        record.Naics = "541512 | Computer Systems Design Services";

        var entity = UsaSpendingAwardMapper.Map(record, Guid.NewGuid());

        entity.NaicsCode.Should().Be("541512");
    }

    [Fact]
    public void Map_truncating_an_over_long_recipient_name_does_not_split_a_surrogate_pair()
    {
        // Contract: truncation must yield a well-formed prefix. Slicing by char index at
        // the 512 cap can orphan a surrogate pair, producing invalid UTF-16 Postgres rejects.
        var record = SampleRecord();
        record.RecipientName = new string('A', 511) + "😀"; // 511 chars + 😀 = 513 > 512

        var entity = UsaSpendingAwardMapper.Map(record, Guid.NewGuid());

        entity.RecipientName.Should().NotBeNull();
        char.IsHighSurrogate(entity.RecipientName![^1])
            .Should()
            .BeFalse("truncating at the 512-char cap must not leave an orphaned high surrogate");
    }
}
