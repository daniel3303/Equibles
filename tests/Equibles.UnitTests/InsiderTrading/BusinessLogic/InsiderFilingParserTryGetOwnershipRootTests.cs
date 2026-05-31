using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserTryGetOwnershipRootTests
{
    // TryGetOwnershipRoot is the reprocess pipeline's entry point: it sanitizes
    // and parses a cached or freshly-fetched ownership payload, returning null
    // for anything that isn't a parseable ownershipDocument. Null is the signal
    // the reprocess manager uses to fall back to a re-fetch / record a failure,
    // so the legacy and malformed cases must return null rather than throw.

    [Fact]
    public void TryGetOwnershipRoot_LegacyNonXmlFiling_ReturnsNull()
    {
        var legacy = """
            -----BEGIN PRIVACY-ENHANCED MESSAGE-----
            <TYPE>4
            <TEXT>
            TICKER  SYMBOL  REPORTING-OWNER
            -----END PRIVACY-ENHANCED MESSAGE-----
            """;

        InsiderFilingParser.TryGetOwnershipRoot(legacy).Should().BeNull();
    }

    [Fact]
    public void TryGetOwnershipRoot_MalformedOwnershipXml_ReturnsNull()
    {
        // Has the ownershipDocument marker but is not well-formed (unclosed tag).
        var malformed = "<ownershipDocument><reportingOwner></ownershipDocument>";

        InsiderFilingParser.TryGetOwnershipRoot(malformed).Should().BeNull();
    }

    [Fact]
    public void TryGetOwnershipRoot_ValidOwnershipXml_ReturnsRoot()
    {
        var xml = "<ownershipDocument><reportingOwner /></ownershipDocument>";

        var root = InsiderFilingParser.TryGetOwnershipRoot(xml);

        root.Should().NotBeNull();
        root!.Name.LocalName.Should().Be("ownershipDocument");
    }

    [Fact]
    public void TryGetOwnershipRoot_SgmlEnvelopedXml_StripsEnvelopeAndParses()
    {
        // The cached/fetched payload can still carry the SEC <XML> SGML envelope;
        // sanitize must strip it so the ownershipDocument parses.
        var enveloped = "<XML><ownershipDocument><reportingOwner /></ownershipDocument></XML>";

        var root = InsiderFilingParser.TryGetOwnershipRoot(enveloped);

        root.Should().NotBeNull();
        root!.Name.LocalName.Should().Be("ownershipDocument");
    }
}
