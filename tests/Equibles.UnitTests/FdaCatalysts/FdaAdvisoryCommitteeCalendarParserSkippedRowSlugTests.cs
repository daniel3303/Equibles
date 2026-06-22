using System;
using Equibles.FdaCatalysts.BusinessLogic;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.FdaCatalysts;

public class FdaAdvisoryCommitteeCalendarParserSkippedRowSlugTests
{
    // Contract (class doc): a row without a parseable Start Date "carr[ies] no authoritative date
    // ... and no stable natural key", so it is skipped and must leave no trace. When the same
    // meeting is listed twice and the FIRST row has no parseable Start Date (here empty), that
    // skipped row must not consume the slug — the later, fully valid row for the same meeting must
    // still be captured. Adversarial: the parser registers the slug as "seen" before validating the
    // Start Date, so a date-less first row suppresses the valid duplicate and the meeting is lost.
    [Fact]
    public void Parse_FirstRowWithoutStartDateSharesSlug_StillCapturesTheValidRow()
    {
        const string html =
            @"<table class='lcds-datatable--advisory-committee-calendar'>
  <thead><tr><th>Start Date</th><th>End Date</th><th>Meeting</th><th>Center</th></tr></thead>
  <tbody>
    <tr>
      <td></td>
      <td></td>
      <td><a href='/advisory-committees/advisory-committee-calendar/april-15-2026-vaccines-advisory-committee-meeting'>April 15, 2026: Vaccines Advisory Committee (date TBD)</a></td>
      <td>Center for Biologics Evaluation and Research</td>
    </tr>
    <tr>
      <td>04/15/2026 08:00 AM EDT</td>
      <td>04/15/2026 04:00 PM EDT</td>
      <td><a href='/advisory-committees/advisory-committee-calendar/april-15-2026-vaccines-advisory-committee-meeting'>April 15, 2026: Vaccines Advisory Committee</a></td>
      <td>Center for Biologics Evaluation and Research</td>
    </tr>
  </tbody>
</table>";

        var catalyst = FdaAdvisoryCommitteeCalendarParser
            .Parse(html)
            .Should()
            .ContainSingle()
            .Subject;

        catalyst.MeetingDate.Should().Be(new DateOnly(2026, 4, 15));
        catalyst.SourceReference.Should().Be("april-15-2026-vaccines-advisory-committee-meeting");
    }
}
