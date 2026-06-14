using System;
using Equibles.FdaCatalysts.BusinessLogic;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.FdaCatalysts;

public class FdaAdvisoryCommitteeCalendarParserSlugHrefCleanupTests
{
    // Contract: the slug (SourceReference) is the stable per-meeting natural key that makes
    // re-imports idempotent, so a query string, a fragment, and a trailing slash on the meeting
    // link must all be stripped — none may leak into the key, or the same meeting would re-import
    // as a duplicate. Existing slug coverage only exercises a clean path-only href.
    [Fact]
    public void Parse_MeetingHrefWithQueryFragmentAndTrailingSlash_YieldsCleanSlug()
    {
        const string html =
            @"<table class='lcds-datatable--advisory-committee-calendar'>
  <thead><tr><th>Start Date</th><th>End Date</th><th>Meeting</th><th>Center</th></tr></thead>
  <tbody>
    <tr>
      <td>10/22/2026 08:00 AM EDT</td>
      <td></td>
      <td><a href='/advisory-committees/advisory-committee-calendar/october-22-2026-cardiovascular-and-renal-drugs-advisory-committee-meeting/?utm_source=newsletter#agenda'>October 22, 2026: Cardiovascular and Renal Drugs Advisory Committee</a></td>
      <td>Center for Drug Evaluation and Research</td>
    </tr>
  </tbody>
</table>";

        var catalyst = FdaAdvisoryCommitteeCalendarParser
            .Parse(html)
            .Should()
            .ContainSingle()
            .Subject;

        catalyst
            .SourceReference.Should()
            .Be("october-22-2026-cardiovascular-and-renal-drugs-advisory-committee-meeting");
    }
}
