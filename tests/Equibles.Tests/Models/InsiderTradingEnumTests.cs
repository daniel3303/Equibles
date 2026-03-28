using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Equibles.Core.Extensions;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.Tests.Models;

public class InsiderTradingEnumTests {
    [Fact]
    public void TransactionCode_Purchase_ReturnsCorrectDisplayName() {
        TransactionCode.Purchase.NameForHumans().Should().Be("Purchase");
    }

    [Fact]
    public void TransactionCode_TaxPayment_ReturnsMultiWordDisplayName() {
        TransactionCode.TaxPayment.NameForHumans().Should().Be("Tax Payment");
    }

    [Theory]
    [InlineData(TransactionCode.Purchase, "Purchase")]
    [InlineData(TransactionCode.Sale, "Sale")]
    [InlineData(TransactionCode.Award, "Award")]
    [InlineData(TransactionCode.Conversion, "Conversion")]
    [InlineData(TransactionCode.Exercise, "Exercise")]
    [InlineData(TransactionCode.TaxPayment, "Tax Payment")]
    [InlineData(TransactionCode.Expiration, "Expiration")]
    [InlineData(TransactionCode.Gift, "Gift")]
    [InlineData(TransactionCode.Inheritance, "Inheritance")]
    [InlineData(TransactionCode.Discretionary, "Discretionary")]
    [InlineData(TransactionCode.Other, "Other")]
    public void TransactionCode_AllValues_HaveCorrectDisplayName(TransactionCode code, string expected) {
        code.NameForHumans().Should().Be(expected);
    }

    [Fact]
    public void AcquiredDisposed_Acquired_ReturnsCorrectDisplayName() {
        AcquiredDisposed.Acquired.NameForHumans().Should().Be("Acquired");
    }

    [Fact]
    public void AcquiredDisposed_Disposed_ReturnsCorrectDisplayName() {
        AcquiredDisposed.Disposed.NameForHumans().Should().Be("Disposed");
    }

    [Fact]
    public void OwnershipNature_Direct_ReturnsCorrectDisplayName() {
        OwnershipNature.Direct.NameForHumans().Should().Be("Direct");
    }

    [Fact]
    public void OwnershipNature_Indirect_ReturnsCorrectDisplayName() {
        OwnershipNature.Indirect.NameForHumans().Should().Be("Indirect");
    }

    [Fact]
    public void TransactionCode_AllValues_HaveDisplayAttribute() {
        var values = Enum.GetValues<TransactionCode>();
        values.Should().HaveCount(11);

        foreach (var value in values) {
            var member = typeof(TransactionCode).GetMember(value.ToString()).First();
            member.GetCustomAttribute<DisplayAttribute>().Should().NotBeNull(
                because: $"{value} should have a [Display] attribute");
        }
    }

    [Fact]
    public void AcquiredDisposed_AllValues_HaveDisplayAttribute() {
        var values = Enum.GetValues<AcquiredDisposed>();
        values.Should().HaveCount(2);

        foreach (var value in values) {
            var member = typeof(AcquiredDisposed).GetMember(value.ToString()).First();
            member.GetCustomAttribute<DisplayAttribute>().Should().NotBeNull(
                because: $"{value} should have a [Display] attribute");
        }
    }

    [Fact]
    public void OwnershipNature_AllValues_HaveDisplayAttribute() {
        var values = Enum.GetValues<OwnershipNature>();
        values.Should().HaveCount(2);

        foreach (var value in values) {
            var member = typeof(OwnershipNature).GetMember(value.ToString()).First();
            member.GetCustomAttribute<DisplayAttribute>().Should().NotBeNull(
                because: $"{value} should have a [Display] attribute");
        }
    }
}
