using Equibles.FdaCatalysts.BusinessLogic;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.FdaCatalysts;

public class FdaAdvisoryCommitteeCalendarParserMissingColumnTests
{
    // The parser maps fields by resolving each column from the header, so a required column it
    // cannot locate (here the Meeting column, which carries the slug and title) leaves it with no
    // way to key or name a row. It must return empty rather than read a wrong cell by position —
    // a guard against a future FDA table layout that drops or renames a structural column.
    [Fact]
    public void Parse_HeaderMissingTheMeetingColumn_ReturnsEmpty()
    {
        const string html =
            @"<table class='lcds-datatable--advisory-committee-calendar'>
  <thead><tr><th>Start Date</th><th>End Date</th><th>Center</th></tr></thead>
  <tbody>
    <tr>
      <td>06/15/2026 08:00 AM EDT</td>
      <td>06/15/2026 04:00 PM EDT</td>
      <td>Center for Drug Evaluation and Research</td>
    </tr>
  </tbody>
</table>";

        var catalysts = FdaAdvisoryCommitteeCalendarParser.Parse(html);

        catalysts.Should().BeEmpty();
    }
}
