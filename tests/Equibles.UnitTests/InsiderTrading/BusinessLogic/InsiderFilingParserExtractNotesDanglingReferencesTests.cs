using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserExtractNotesDanglingReferencesTests
{
    // Contract (per ExtractNotes' doc): resolve every referenced footnote to its text,
    // in document order, de-duplicated by id. A real filing can carry malformed or
    // dangling references — a <footnoteId> with no id, an empty id, or an id with no
    // matching <footnote> entry. None of those resolve to text, so each must be dropped
    // without throwing while a following well-formed reference still resolves.
    [Fact]
    public void ExtractNotes_MalformedAndDanglingReferences_AreSkippedAndValidStillResolves()
    {
        var footnotes = new Dictionary<string, string> { ["F1"] = "Shares held in a trust." };
        var row = new XElement(
            "nonDerivativeTransaction",
            new XElement("footnoteId"), // missing id attribute
            new XElement("footnoteId", new XAttribute("id", "")), // empty id
            new XElement("footnoteId", new XAttribute("id", "F9")), // id absent from the map
            new XElement("footnoteId", new XAttribute("id", "F1")) // the only resolvable one
        );

        var notes = InsiderFilingParser.ExtractNotes(row, footnotes);

        notes.Should().Equal("Shares held in a trust.");
    }
}
