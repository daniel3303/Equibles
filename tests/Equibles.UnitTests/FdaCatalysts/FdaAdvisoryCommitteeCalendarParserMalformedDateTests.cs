using System;
using Equibles.FdaCatalysts.BusinessLogic;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.FdaCatalysts;

public class FdaAdvisoryCommitteeCalendarParserMalformedDateTests
{
    // Contract: a row carries an authoritative date only when its Start Date column parses as
    // the calendar's MM/dd/yyyy format. The first row's Start Date is a valid-looking ISO date
    // (2026-07-04) — a lenient parser would silently place it on the timeline as July 4; the
    // strict format contract must reject it and skip the row, while the sibling MM/dd/yyyy row
    // still maps. This pins format strictness, not just the empty-Start-Date case.
    [Fact]
    public void Parse_RowWithWrongFormatStartDate_IsSkippedWhileValidRowSurvives()
    {
        const string html =
            @"<table class='lcds-datatable--advisory-committee-calendar'>
  <thead><tr><th>Start Date</th><th>End Date</th><th>Meeting</th><th>Center</th></tr></thead>
  <tbody>
    <tr>
      <td>2026-07-04</td>
      <td></td>
      <td><a href='/advisory-committees/advisory-committee-calendar/iso-dated-pediatric-advisory-committee-meeting'>ISO-dated: Pediatric Advisory Committee</a></td>
      <td>Center for Drug Evaluation and Research</td>
    </tr>
    <tr>
      <td>09/10/2026 08:00 AM EDT</td>
      <td></td>
      <td><a href='/advisory-committees/advisory-committee-calendar/september-10-2026-oncologic-drugs-advisory-committee-meeting'>September 10, 2026: Oncologic Drugs Advisory Committee</a></td>
      <td>Center for Drug Evaluation and Research</td>
    </tr>
  </tbody>
</table>";

        var catalyst = FdaAdvisoryCommitteeCalendarParser
            .Parse(html)
            .Should()
            .ContainSingle()
            .Subject;

        catalyst.MeetingDate.Should().Be(new DateOnly(2026, 9, 10));
        catalyst
            .SourceReference.Should()
            .Be("september-10-2026-oncologic-drugs-advisory-committee-meeting");
    }
}
