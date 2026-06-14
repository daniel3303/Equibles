using Equibles.FdaCatalysts.BusinessLogic;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.FdaCatalysts;

public class FdaAdvisoryCommitteeCalendarParserDuplicateSlugTests
{
    // The per-meeting slug is the natural key the worker upserts on, so the parser must dedup it
    // within a single page: a calendar that lists the same meeting twice (here the second row's
    // anchor differs only by a tracking query string the slug extractor strips) must yield one
    // catalyst, not a duplicate pair, so the same render cannot insert the meeting twice.
    [Fact]
    public void Parse_DuplicateMeetingSlug_KeepsOnlyTheFirstOccurrence()
    {
        const string html =
            @"<table class='lcds-datatable--advisory-committee-calendar'>
  <thead><tr><th>Start Date</th><th>End Date</th><th>Meeting</th><th>Center</th></tr></thead>
  <tbody>
    <tr>
      <td>03/10/2026 08:00 AM EDT</td>
      <td>03/10/2026 04:00 PM EDT</td>
      <td><a href='/advisory-committees/advisory-committee-calendar/march-10-2026-oncologic-drugs-advisory-committee-meeting'>March 10, 2026: Oncologic Drugs Advisory Committee</a></td>
      <td>Center for Drug Evaluation and Research</td>
    </tr>
    <tr>
      <td>03/10/2026 08:00 AM EDT</td>
      <td>03/10/2026 04:00 PM EDT</td>
      <td><a href='/advisory-committees/advisory-committee-calendar/march-10-2026-oncologic-drugs-advisory-committee-meeting?utm=dup'>March 10, 2026: Oncologic Drugs Advisory Committee (duplicate listing)</a></td>
      <td>Center for Drug Evaluation and Research</td>
    </tr>
  </tbody>
</table>";

        var catalysts = FdaAdvisoryCommitteeCalendarParser.Parse(html);

        catalysts
            .Should()
            .ContainSingle()
            .Which.SourceReference.Should()
            .Be("march-10-2026-oncologic-drugs-advisory-committee-meeting");
    }
}
