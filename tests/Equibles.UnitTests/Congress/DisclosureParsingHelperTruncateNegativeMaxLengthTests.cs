using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperTruncateNegativeMaxLengthTests
{
    // Contract: Truncate(value, maxLength) returns a string no longer than
    // maxLength. A negative maxLength is semantically equivalent to zero —
    // there is no length the caller is willing to accept. The implementation
    // passes maxLength directly into the Range indexer (value[..end]) without
    // clamping; a negative end index is not a valid Range bound and throws
    // ArgumentOutOfRangeException at runtime.
    [Fact]
    public void Truncate_NegativeMaxLength_DoesNotThrow()
    {
        var act = () => DisclosureParsingHelper.Truncate("hello", -1);

        act.Should().NotThrow();
    }
}
