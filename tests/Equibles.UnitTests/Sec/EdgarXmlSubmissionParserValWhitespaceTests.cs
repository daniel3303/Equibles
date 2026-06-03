using System.Xml.Linq;
using Equibles.Sec.HostedService.Helpers;

namespace Equibles.UnitTests.Sec;

public class EdgarXmlSubmissionParserValWhitespaceTests
{
    [Fact]
    public void Val_PresentButWhitespaceOnlyElement_ReturnsNull()
    {
        // Contract (doc): "Trimmed text of the named child element, or null when missing or empty."
        // A present element holding only whitespace must read as absent (null) so downstream form
        // processors treat blank optional fields uniformly. Val is otherwise unit-untested; a
        // regression dropping the Trim/IsNullOrEmpty would return "   " and break null-checks.
        var parent = XElement.Parse("<root><field>   \n  </field></root>");

        EdgarXmlSubmissionParser.Val(parent, "field").Should().BeNull();
    }
}
