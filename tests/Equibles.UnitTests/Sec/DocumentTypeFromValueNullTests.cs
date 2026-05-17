using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Sec;

public class DocumentTypeFromValueNullTests
{
    // Contract: FromValue is a lookup that returns null when there is no match
    // (cf. FromValue_UnknownValue_ReturnsNull). Its sole caller is the EF value
    // converter `DocumentType.FromValue(v) ?? new DocumentType(v)`, which gets
    // v == null for a NULL column — so null input must return null, not throw.
    [Fact]
    public void FromValue_NullValue_ReturnsNullInsteadOfThrowing()
    {
        DocumentType.FromValue(null).Should().BeNull();
    }
}
