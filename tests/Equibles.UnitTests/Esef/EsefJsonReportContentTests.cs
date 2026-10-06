using System.Text;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.HostedService.Services;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace Equibles.UnitTests.Esef;

public class EsefJsonReportContentTests
{
    private const string Lei = "2549001EPXH6NK7I2R78";
    private const string NoteId = "f1__s9__7__242-1";

    private static JObject Report() =>
        JObject.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "TestAssets",
                    "Esef",
                    "better-collective-2025-json-notes-excerpt.json"
                )
            )
        );

    private static string Build(string json) =>
        Encoding.UTF8.GetString(
            EsefJsonReportContent.Build(
                json,
                Lei,
                new DateOnly(2025, 12, 31),
                new SecDocumentHtmlNormalizer(),
                new SecDocumentHtmlToMarkdownConverter()
            )
        );

    [Fact]
    public void Build_CapturedBorrowingNote_PreservesAmountsTermsAndSourceDates()
    {
        var report = Report();
        var original = report.ToString();

        var text = Build(original);

        text.Should().Contain("Better Collective has drawn 259.7 mEUR");
        text.Should().Contain("total committed club facility of 319 mEUR");
        text.Should().Contain("80 mEUR higher accordion option");
        text.Should().Contain("expiry at the end of October 2028");
        text.Should().Contain("December 31, 2025");
        text.Should().NotContain("2026-01-01T00:00:00");
        text.Should().NotContain("1074121000");
        report.ToString().Should().Be(original);
    }

    [Theory]
    [InlineData("entity", "scheme:529900S21EQ1BO4ESM68")]
    [InlineData("period", "2024-01-01T00:00:00/2025-01-01T00:00:00")]
    [InlineData("period", "2026-01-01T00:00:00/2027-01-01T00:00:00")]
    [InlineData("period", "2025-02-30T00:00:00/2026-01-01T00:00:00")]
    [InlineData("period", "2025-01-01/2026-01-01T00:00:00")]
    [InlineData("period", "2026-01-01T00:00:00/2026-01-01T00:00:00")]
    [InlineData("period", "2025-01-01T00:00:00/2026-01-01T12:00:00")]
    [InlineData("concept", "BET:DisclosureOfBorrowingsExplanatory")]
    [InlineData("concept", "ifrs-full:Borrowings")]
    [InlineData("concept", "ifrs-full:Not a valid QNameExplanatory")]
    [InlineData("concept", "ifrs-full:1Explanatory")]
    [InlineData("concept", "ifrs-full:extra:DisclosureOfBorrowingsExplanatory")]
    [InlineData("unit", "iso4217:EUR")]
    [InlineData(
        "ifrs-full:ConsolidatedAndSeparateFinancialStatementsAxis",
        "ifrs-full:SeparateMember"
    )]
    public void Build_IneligibleNote_DoesNotBorrowContextFromNumericFacts(
        string key,
        string value
    )
    {
        var report = Report();
        report["facts"][NoteId]["dimensions"][key] = value;

        Build(report.ToString()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("concept", "ifrs-full", "DisclosureOfBorrowingsExplanatory")]
    [InlineData("entity", "scheme", Lei)]
    public void Build_InvalidNamespacePrefix_ExcludesTheDisclosure(
        string dimension,
        string prefix,
        string localName
    )
    {
        var report = Report();
        report["documentInfo"]["namespaces"]["bad prefix"] =
            report["documentInfo"]["namespaces"][prefix];
        report["facts"][NoteId]["dimensions"][dimension] = "bad prefix:" + localName;

        Build(report.ToString()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://xbrl.ifrs.org.evil.example/taxonomy/2024-03-27/ifrs-full")]
    [InlineData("ftp://xbrl.ifrs.org/taxonomy/2024-03-27/ifrs-full")]
    [InlineData("https://xbrl.ifrs.org/taxonomy/2024-03-27/fake")]
    public void Build_SpoofedDisclosureNamespace_IsExcluded(string address)
    {
        var report = Report();
        report["documentInfo"]["namespaces"]["notes"] = address;
        report["facts"][NoteId]["dimensions"]["concept"] =
            "notes:DisclosureOfBorrowingsExplanatory";

        Build(report.ToString()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("2025-01-01T00:00:00/2026-01-01T00:00:00")]
    [InlineData("2026-01-01T00:00:00")]
    public void Build_TrustedNamespaceAliasAndMatchingPeriod_PreservesDisclosure(string period)
    {
        var report = Report();
        report["documentInfo"]["namespaces"]["notes"] =
            report["documentInfo"]["namespaces"]["ifrs-full"];
        report["facts"][NoteId]["dimensions"]["concept"] =
            "notes:DisclosureOfBorrowingsExplanatory";
        report["facts"][NoteId]["dimensions"]["period"] = period;

        Build(report.ToString()).Should().Contain("319 mEUR");
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("trailing")]
    [InlineData("wrong-owner")]
    [InlineData("wrong-period")]
    public void Build_InvalidEnvelope_RejectsRatherThanPublishingNotes(string failure)
    {
        var json = Report().ToString();
        json = failure switch
        {
            "duplicate" => json.Replace(
                "\"documentInfo\":",
                "\"documentInfo\": {}, \"documentInfo\":"
            ),
            "trailing" => json + "{}",
            "wrong-owner" => json.Replace(Lei, "529900S21EQ1BO4ESM68"),
            _ => json.Replace("2026-01-01T00:00:00", "2027-01-01T00:00:00"),
        };

        var act = () => Build(json);

        act.Should()
            .Throw<Exception>()
            .Where(exception => exception is JsonException || exception is InvalidDataException);
    }

    [Fact]
    public void Build_CombinedMarkupExceedsLimit_DoesNotPublishATruncatedSubset()
    {
        var report = Report();
        var notes = (JObject)report["facts"];
        notes[NoteId]["value"] =
            "<p>" + new string('x', EsefReportContent.MaxRetrievalHtmlChars / 2) + "</p>";
        notes["second-note"] = notes[NoteId].DeepClone();

        Build(report.ToString()).Should().BeEmpty();
    }

    [Fact]
    public void Build_ConverterProducesNothing_FailsSoRecoveryCanRetry()
    {
        var converter = Substitute.For<ISecDocumentHtmlToMarkdownConverter>();
        var act = () => EsefJsonReportContent.Build(
            Report().ToString(),
            Lei,
            new DateOnly(2025, 12, 31),
            new SecDocumentHtmlNormalizer(),
            converter
        );

        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_OneConversionFails_DoesNotSettleTheSuccessfulSubset(string failedText)
    {
        var report = Report();
        report["facts"]["second-note"] = report["facts"][NoteId].DeepClone();
        var converter = Substitute.For<ISecDocumentHtmlToMarkdownConverter>();
        converter.Convert(Arg.Any<string>()).Returns("First note", failedText);
        var act = () => EsefJsonReportContent.Build(
            report.ToString(),
            Lei,
            new DateOnly(2025, 12, 31),
            new SecDocumentHtmlNormalizer(),
            converter
        );

        act.Should().Throw<InvalidDataException>();
        converter.Received(2).Convert(Arg.Any<string>());
    }
}
