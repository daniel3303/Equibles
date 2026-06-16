using System;
using Equibles.FdaCatalysts.BusinessLogic;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.FdaCatalysts;

public class FdaAdvisoryCommitteeCalendarParserAbsoluteHrefTests
{
    // Contract: SourceUrl is the meeting link made absolute. When the calendar already renders a
    // fully-qualified href, it must pass through unchanged — never be re-prefixed into
    // "https://www.fda.govhttps://...". Existing coverage only exercises a relative href.
    [Fact]
    public void Parse_AbsoluteMeetingHref_PassesThroughWithoutDoublePrefixing()
    {
        const string href =
            "https://www.fda.gov/advisory-committees/advisory-committee-calendar/december-3-2026-anesthetic-and-analgesic-drug-products-advisory-committee-meeting";
        var html =
            @"<table class='lcds-datatable--advisory-committee-calendar'>
  <thead><tr><th>Start Date</th><th>End Date</th><th>Meeting</th><th>Center</th></tr></thead>
  <tbody>
    <tr>
      <td>12/03/2026 08:00 AM EST</td>
      <td></td>
      <td><a href='"
            + href
            + @"'>December 3, 2026: Anesthetic and Analgesic Drug Products Advisory Committee</a></td>
      <td>Center for Drug Evaluation and Research</td>
    </tr>
  </tbody>
</table>";

        var catalyst = FdaAdvisoryCommitteeCalendarParser
            .Parse(html)
            .Should()
            .ContainSingle()
            .Subject;

        catalyst.SourceUrl.Should().Be(href);
    }
}
