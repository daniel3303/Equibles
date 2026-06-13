using System.Reflection;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// A page break reprints the PTR column-header block. PdfPig renders it as a line with no
/// colon, asterisk or transaction anchor, so <c>IsContinuationLine</c> used to classify it as
/// a continuation and splice it into the preceding trade — appending the header words to the
/// asset name and pulling the "Cap. Gains &gt; $200?" threshold into the amount, producing
/// inverted ranges like "$50,001-$200" (#3378). The reprinted header must not be a continuation,
/// while a genuine wrapped asset line still must be.
/// </summary>
public class HouseDisclosureClientTableHeaderContinuationTests
{
    private static bool IsContinuationLine(string line) =>
        (bool)
            typeof(HouseDisclosureClient)
                .GetMethod("IsContinuationLine", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [line])!;

    [Fact]
    public void IsContinuationLine_ReprintedPtrColumnHeader_NotTreatedAsContinuation()
    {
        const string header =
            "ID Owner Asset Transaction Date Notification Amount Cap. Type Date Gains >";

        IsContinuationLine(header)
            .Should()
            .BeFalse("a reprinted PTR column header must not be spliced into the preceding trade");
    }

    [Fact]
    public void IsContinuationLine_WrappedAssetName_StillContinuation()
    {
        // A genuine continuation carries at most one header-ish word and must keep flowing
        // into the preceding trade's asset name.
        IsContinuationLine("Class A Common Stock").Should().BeTrue();
    }
}
