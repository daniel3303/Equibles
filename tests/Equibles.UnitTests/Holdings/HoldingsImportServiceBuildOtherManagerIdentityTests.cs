using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceBuildOtherManagerIdentityTests
{
    private static OtherManagerIdentity Invoke(Dictionary<string, string> row, string name)
    {
        var method = typeof(HoldingsImportService).GetMethod(
            "BuildOtherManagerIdentity",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        return (OtherManagerIdentity)method!.Invoke(null, [row, name]);
    }

    [Fact]
    public void BuildOtherManagerIdentity_LegacyThreeColumnRow_YieldsNameWithNullIdentifiers()
    {
        // Every archive written before this lane existed — and the Schedule 13D/G synthetic one,
        // which ships a header-only section — carries only ACCESSION_NUMBER/SEQUENCENUMBER/NAME.
        // A reader that assumed the identifier columns were present would throw or, worse, store
        // empty strings that later look like filed identifiers and get joined on. The row must
        // still parse, identifiers absent.
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ACCESSION_NUMBER"] = "0000950123-24-007578",
            ["SEQUENCENUMBER"] = "1",
            ["NAME"] = "LEGACY ADVISORS",
        };

        var identity = Invoke(row, "LEGACY ADVISORS");

        identity.Name.Should().Be("LEGACY ADVISORS");
        identity.Cik.Should().BeNull();
        identity.Form13FFileNumber.Should().BeNull();
        identity.CrdNumber.Should().BeNull();
        identity.SecFileNumber.Should().BeNull();
    }

    [Fact]
    public void BuildOtherManagerIdentity_FullRow_TrimsTheCikToTheStoredSpelling()
    {
        // Filer CIKs are stored with leading zeros stripped. The data set pads them, so an
        // untrimmed value compares unequal to the very holder it identifies and the manager stays
        // unlinked despite carrying a perfectly good CIK.
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CIK"] = "0000769993",
            ["FORM13FFILENUMBER"] = "028-00687",
            ["CRDNUMBER"] = "000000361",
            ["SECFILENUMBER"] = "801-16048",
            ["NAME"] = "GOLDMAN SACHS & CO. LLC",
        };

        var identity = Invoke(row, "GOLDMAN SACHS & CO. LLC");

        identity.Cik.Should().Be("769993");
        identity.Form13FFileNumber.Should().Be("028-00687");
        identity.CrdNumber.Should().Be("000000361");
        identity.SecFileNumber.Should().Be("801-16048");
    }

    [Fact]
    public void BuildOtherManagerIdentity_BlankIdentifierColumns_StayNullRatherThanBecomingEmpty()
    {
        // The SEC keeps these columns present and empty rather than omitting them, so every one of
        // them arrives as "" instead of null. An empty string is not an absent identifier — it is a
        // value that compares equal to every other blank, so joining on it would collide every
        // identifier-less manager into a single institution.
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CIK"] = "   ",
            ["FORM13FFILENUMBER"] = "",
            ["CRDNUMBER"] = "",
            ["SECFILENUMBER"] = "",
            ["NAME"] = "BLANK IDENTIFIER ADVISORS",
        };

        var identity = Invoke(row, "BLANK IDENTIFIER ADVISORS");

        identity.Cik.Should().BeNull();
        identity.Form13FFileNumber.Should().BeNull();
        identity.CrdNumber.Should().BeNull();
        identity.SecFileNumber.Should().BeNull();
    }

    [Fact]
    public void BuildOtherManagerIdentity_OverlongValues_AreClampedToTheirColumnBounds()
    {
        // The other-manager table is free text out of a filer's own submission. A value past the
        // destination column's bound aborts the whole insert with a 22001, losing every manager in
        // the batch; keeping the prefix beats losing the batch.
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FORM13FFILENUMBER"] = new string('F', 80),
            ["CRDNUMBER"] = new string('C', 80),
            ["SECFILENUMBER"] = new string('S', 80),
        };

        var identity = Invoke(row, new string('N', 400));

        identity.Name.Should().HaveLength(256);
        identity.Form13FFileNumber.Should().HaveLength(32);
        identity.CrdNumber.Should().HaveLength(32);
        identity.SecFileNumber.Should().HaveLength(32);
    }
}
