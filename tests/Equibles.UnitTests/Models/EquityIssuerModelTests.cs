using Equibles.CommonStocks.Data.Models;

namespace Equibles.UnitTests.Models;

public class EquityIssuerModelTests
{
    [Fact]
    public void SecondaryCiks_ExplicitNull_RemainsAnEmptyCollection()
    {
        var issuer = new EquityIssuer { SecondaryCiks = null };
        issuer.SecondaryCiks.Should().NotBeNull().And.BeEmpty();
    }
}
