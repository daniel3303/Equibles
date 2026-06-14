using Equibles.FdaCatalysts.BusinessLogic;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.FdaCatalysts;

public class FdaAdvisoryCommitteeCalendarParserAnchorlessRowTests
{
    // The per-meeting anchor carries the slug (the upsert natural key) and the title, so the
    // parser documents that a row without it is skipped even when its other columns parse. A row
    // whose Meeting cell is plain text (no link) must be dropped, not turned into a slug-less,
    // title-less catalyst — otherwise the worker would upsert on an empty key.
    [Fact]
    public void Parse_RowWithStartDateButNoMeetingAnchor_IsSkipped()
    {
        const string html =
            @"<table class='lcds-datatable--advisory-committee-calendar'>
  <thead><tr><th>Start Date</th><th>End Date</th><th>Meeting</th><th>Center</th></tr></thead>
  <tbody>
    <tr>
      <td>04/15/2026 08:00 AM EDT</td>
      <td>04/15/2026 04:00 PM EDT</td>
      <td>Pending Advisory Committee Meeting (details to follow)</td>
      <td>Center for Drug Evaluation and Research</td>
    </tr>
    <tr>
      <td>05/20/2026 08:00 AM EDT</td>
      <td>05/20/2026 04:00 PM EDT</td>
      <td><a href='/advisory-committees/advisory-committee-calendar/may-20-2026-cardiovascular-and-renal-drugs-advisory-committee-meeting'>May 20, 2026: Cardiovascular and Renal Drugs Advisory Committee</a></td>
      <td>Center for Drug Evaluation and Research</td>
    </tr>
  </tbody>
</table>";

        var catalysts = FdaAdvisoryCommitteeCalendarParser.Parse(html);

        catalysts
            .Should()
            .ContainSingle("the anchor-less row carries no slug or title and is dropped")
            .Which.SourceReference.Should()
            .Be("may-20-2026-cardiovascular-and-renal-drugs-advisory-committee-meeting");
    }
}
