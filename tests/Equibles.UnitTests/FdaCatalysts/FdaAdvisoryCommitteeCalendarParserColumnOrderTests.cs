using System;
using Equibles.FdaCatalysts.BusinessLogic;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.FdaCatalysts;

public class FdaAdvisoryCommitteeCalendarParserColumnOrderTests
{
    // The parser resolves each column from the header text, not a fixed position, so a calendar
    // whose columns are rendered in a different order still maps every field to the right
    // property. Columns here are Center, Meeting, Start Date, End Date — none in canonical order.
    [Fact]
    public void Parse_ColumnsInNonCanonicalOrder_MapsEveryFieldByHeader()
    {
        const string html =
            @"<table class='lcds-datatable--advisory-committee-calendar'>
  <thead><tr><th>Center</th><th>Meeting</th><th>Start Date</th><th>End Date</th></tr></thead>
  <tbody>
    <tr>
      <td>Center for Biologics Evaluation and Research</td>
      <td><a href='/advisory-committees/advisory-committee-calendar/may-14-15-2026-cellular-tissue-and-gene-therapies-advisory-committee-meeting'>May 14-15, 2026: Cellular, Tissue, and Gene Therapies Advisory Committee</a></td>
      <td>05/14/2026 09:00 AM EDT</td>
      <td>05/15/2026 05:00 PM EDT</td>
    </tr>
  </tbody>
</table>";

        var catalyst = FdaAdvisoryCommitteeCalendarParser
            .Parse(html)
            .Should()
            .ContainSingle()
            .Subject;

        catalyst.MeetingDate.Should().Be(new DateOnly(2026, 5, 14));
        catalyst.EndDate.Should().Be(new DateOnly(2026, 5, 15));
        catalyst.Center.Should().Be("Center for Biologics Evaluation and Research");
        catalyst
            .Title.Should()
            .Be("May 14-15, 2026: Cellular, Tissue, and Gene Therapies Advisory Committee");
        catalyst
            .SourceReference.Should()
            .Be("may-14-15-2026-cellular-tissue-and-gene-therapies-advisory-committee-meeting");
    }
}
