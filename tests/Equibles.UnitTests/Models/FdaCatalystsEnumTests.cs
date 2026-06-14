using Equibles.Core.Extensions;
using Equibles.FdaCatalysts.Data.Models;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Models;

public class FdaCatalystsEnumTests
{
    // Contract: these are the human-readable catalyst-type labels surfaced to users — the
    // MCP GetFdaCatalysts "Type" column renders CatalystType.NameForHumans() verbatim, and
    // the portal/API use the same mapping. A typo or a dropped [Display] attribute would
    // silently degrade the label (e.g. "PDUFA Decision" falling back to the bare enum name
    // "Pdufa"), so the exact strings are pinned here, not derived from the enum at runtime.
    [Fact]
    public void FdaCatalystType_NameForHumans_ReturnsDocumentedDisplayLabels()
    {
        FdaCatalystType.AdvisoryCommittee.NameForHumans().Should().Be("Advisory Committee Meeting");
        FdaCatalystType.Pdufa.NameForHumans().Should().Be("PDUFA Decision");
        FdaCatalystType.CompleteResponse.NameForHumans().Should().Be("Complete Response Follow-up");
    }
}
