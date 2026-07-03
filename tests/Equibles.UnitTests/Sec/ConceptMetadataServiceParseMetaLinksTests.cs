using System.Text;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// ParseMetaLinks against the real MetaLinks shape (trimmed from an actual ADBE
/// 10-Q artifact): standard-taxonomy tags map to their enum, the filer's own
/// extension tags map to Custom with the extractor's "prefix:LocalName" shape,
/// reference taxonomies we ingest no facts for (ecd, country, …) are skipped,
/// and the crdr attribute becomes the debit/credit balance.
/// </summary>
public class ConceptMetadataServiceParseMetaLinksTests
{
    // Faithful subset of a real MetaLinks.json: instance → document → nsprefix + tag map.
    private const string Sample = """
        {
          "version": "2.2",
          "instance": {
            "adbe-20260529.htm": {
              "nsprefix": "adbe",
              "tag": {
                "us-gaap_ResearchAndDevelopmentExpenseSoftwareExcludingAcquiredInProcessCost": {
                  "crdr": "debit",
                  "lang": { "en-us": { "role": {
                    "label": "Research and Development Expense, Software (Excluding Acquired in Process Cost)",
                    "documentation": "Research and development expense during the period related to the costs of developing and achieving technological feasibility of a computer software product."
                  } } }
                },
                "adbe_ExciseTaxPayableCurrent": {
                  "crdr": "credit",
                  "lang": { "en-us": { "role": {
                    "label": "Excise Tax Payable, Current",
                    "documentation": "Excise Tax Payable, Current"
                  } } }
                },
                "dei_EntityCommonStockSharesOutstanding": {
                  "lang": { "en-us": { "role": {
                    "label": "Entity Common Stock, Shares Outstanding",
                    "documentation": "Indicate number of shares outstanding of each of registrant's classes of common stock."
                  } } }
                },
                "ecd_AllTradingArrangementsMember": {
                  "lang": { "en-us": { "role": { "label": "All Trading Arrangements" } } }
                }
              }
            }
          }
        }
        """;

    private static Dictionary<(FactTaxonomy, string), ConceptMetadataService.TagMetadata> Parse() =>
        ConceptMetadataService
            .ParseMetaLinks(Encoding.UTF8.GetBytes(Sample))
            .ToDictionary(r => r.Pair, r => r.Metadata);

    [Fact]
    public void ParseMetaLinks_StandardTag_CarriesDocumentationAndBalance()
    {
        var parsed = Parse();

        var rd = parsed[
            (
                FactTaxonomy.UsGaap,
                "ResearchAndDevelopmentExpenseSoftwareExcludingAcquiredInProcessCost"
            )
        ];
        rd.Balance.Should().Be(ConceptBalance.Debit);
        rd.Description.Should().Contain("technological feasibility");
        rd.Label.Should().StartWith("Research and Development Expense");
    }

    [Fact]
    public void ParseMetaLinks_FilerExtensionTag_MapsToCustomWithPrefixedTag()
    {
        var parsed = Parse();

        var excise = parsed[(FactTaxonomy.Custom, "adbe:ExciseTaxPayableCurrent")];
        excise.Balance.Should().Be(ConceptBalance.Credit);
        excise.Label.Should().Be("Excise Tax Payable, Current");
    }

    [Fact]
    public void ParseMetaLinks_DeiTag_MapsToDeiWithoutBalance()
    {
        var parsed = Parse();

        var shares = parsed[(FactTaxonomy.Dei, "EntityCommonStockSharesOutstanding")];
        shares.Balance.Should().BeNull();
        shares.Description.Should().Contain("shares outstanding");
    }

    [Fact]
    public void ParseMetaLinks_ReferenceTaxonomyTag_IsSkipped()
    {
        var parsed = Parse();

        parsed.Keys.Should().NotContain(k => k.Item2.Contains("AllTradingArrangementsMember"));
        parsed.Should().HaveCount(3);
    }
}
