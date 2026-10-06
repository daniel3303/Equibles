using System.Text;
using System.Xml;
using System.Xml.Linq;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.HostedService.Services;
using Xunit;

namespace Equibles.UnitTests.Sec;

public class EsefReportEnvelopeTests
{
    private const string Lei = "529900G4A1IKOKC22K56";
    private static readonly DateOnly Period = new(2024, 12, 31);

    [Fact]
    public void RefusesProcessingInstructionsInsideStylesheetsRatherThanDiscardingThem()
    {
        var source = Encoding.UTF8.GetBytes(
            Report()
                .Replace(
                    "<style>",
                    "<style><?xml-stylesheet type='text/css' href='https://example.test/layout.css'?>"
                )
        );
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Discover(source, new(2025, 3, 1))
        );
        Assert.Throws<InvalidDataException>(() => EsefReportEnvelope.ForRetrieval(source));
    }

    [Fact]
    public void RetrievalPreservesQNameSelectorsThatForbidJoiningHiddenContinuations()
    {
        var source = Report()
            .Replace(
                "span.image {background:url(\"data:image/png;base64,AAAA\")}",
                "[name^='financial'] {display:none}"
            )
            .Replace(
                "</body>",
                "<ix:nonNumeric xmlns:financial='https://xbrl.ifrs.org/taxonomy/2024-03-27/ifrs-full' name='financial:Liquidity' contextRef='c-3' escape='true' continuedAt='next'><p>The company's financing con</p></ix:nonNumeric><ix:continuation id='next'><p>sists of a EUR 50 million revolving facility.</p></ix:continuation></body>"
            );
        var bytes = Encoding.UTF8.GetBytes(source);
        EsefReportEnvelope.Discover(bytes, new(2025, 3, 1));
        var retrieval = Encoding.UTF8.GetString(EsefReportEnvelope.ForRetrieval(bytes));
        Assert.Contains("name=\"financial:Liquidity\"", retrieval);
        var normalizer = new SecDocumentHtmlNormalizer();
        var parser = new HtmlParser();
        var before = parser
            .ParseDocument(normalizer.NormalizeFragment(source))
            .QuerySelectorAll("p");
        var after = parser
            .ParseDocument(normalizer.NormalizeFragment(retrieval))
            .QuerySelectorAll("p");
        Assert.Equal(2, before.Length);
        Assert.Equal(before.Select(p => p.TextContent), after.Select(p => p.TextContent));
        Assert.DoesNotContain(after, p => p.QuerySelector("br") != null);
    }

    [Fact]
    public void RetrievalPreservesExternalStylesheetInstructionsThatForbidGeometryJoins()
    {
        const string instruction =
            "<?xml-stylesheet type='text/css' href='https://example.test/layout.css'?>";
        const string css =
            ".pf{position:relative}.pc{position:absolute;left:0px;top:0px}.t{position:absolute;white-space:pre;left:87px;font-size:36px;font-family:serif;transform:matrix(.375,0,0,.375,0,0);transform-origin:0 100%}.y0{bottom:166px}.y1{bottom:148px}.y2{bottom:130px}.y3{bottom:112px}";
        string[] lines =
        [
            "On 20 October 2025, the Group entered into a three-year facility with Allied Irish Banks, plc, comprising a ",
            "term loan drawn to fund the acquisition of the subsidiary. The term loan bears interest at a fixed ",
            "margin over EURIBOR. Transaction costs incurred in connection with the debt facility have ",
            "been capitalised and are being amortised over the term of the facility. ",
        ];
        var prose =
            "<div class='pf'><div class='pc'>"
            + string.Concat(lines.Select((line, i) => $"<div class='t y{i}'>{line}</div>"))
            + "</div></div>";
        var source = Report()
            .Replace("<style>", "<style>" + css)
            .Replace("</body>", prose + "</body>");
        var normalizer = new SecDocumentHtmlNormalizer();
        var parser = new HtmlParser();
        Assert.Single(
            parser.ParseDocument(normalizer.NormalizeFragment(source)).QuerySelectorAll("p")
        );
        var bytes = Encoding.UTF8.GetBytes(instruction + source);
        EsefReportEnvelope.Discover(bytes, new(2025, 3, 1));
        var retrieval = Encoding.UTF8.GetString(EsefReportEnvelope.ForRetrieval(bytes));
        Assert.Contains(instruction, retrieval);
        Assert.Empty(
            parser
                .ParseDocument(normalizer.NormalizeFragment(instruction + source))
                .QuerySelectorAll("p")
        );
        Assert.Empty(
            parser.ParseDocument(normalizer.NormalizeFragment(retrieval)).QuerySelectorAll("p")
        );
    }

    [Fact]
    public void DeclaredInterimWithTrailingYearFiguresCannotEstablishAnnualReport()
    {
        var source = File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Esef",
                "dfds-2026-interim-excerpt.xhtml"
            )
        );
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Discover(source, new(2026, 8, 31))
        );
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Build(source, "549300JZVW1Y1UZ5UK38", new(2026, 6, 30))
        );
    }

    [Theory]
    [InlineData("missing-unit")]
    [InlineData("conflicting-duration")]
    [InlineData("conflicting-instant")]
    public void InvalidAnnualFinancialEvidenceCannotEstablishReport(string shape)
    {
        var xml = XDocument.Parse(Report());
        XNamespace inline = "http://www.xbrl.org/2013/inlineXBRL";
        var fact = xml.Descendants(inline + "nonFraction")
            .Single(element =>
                (string)element.Attribute("contextRef")
                == (shape == "conflicting-instant" ? "c-1" : "c-3")
            );
        if (shape == "missing-unit")
            fact.Attribute("unitRef").Remove();
        else
        {
            var conflict = new XElement(fact);
            conflict.SetAttributeValue("id", "conflicting");
            conflict.Value = "123";
            fact.AddAfterSelf(conflict);
        }
        var source = Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Discover(source, new(2025, 3, 1))
        );
        Assert.Throws<InvalidDataException>(() => EsefReportEnvelope.Build(source, Lei, Period));
    }

    [Fact]
    public void OfficialFrenchPackageExcerptPreservesReportedIssuerPeriodAndAmounts()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "Esef",
            "dila-tff-2026.xhtml"
        );
        var source = File.ReadAllBytes(path);
        var detected = EsefReportEnvelope.Discover(source, new DateOnly(2026, 8, 31));
        Assert.Equal("969500N6BAT2PU986341", detected.LegalEntityIdentifier);
        Assert.Equal(new DateOnly(2026, 4, 30), detected.PeriodEnd);
        var facts = new InlineXbrlParser().Parse(Encoding.UTF8.GetString(detected.Envelope));
        Assert.Equal(2, facts.Count);
        Assert.Contains(
            facts,
            fact => fact.Tag == "Assets" && fact.Value == 938787000m && fact.Unit == "EUR"
        );
        Assert.Contains(
            facts,
            fact => fact.Tag == "RevenueFromContractsWithCustomers" && fact.Value == 312747000m
        );
        Assert.Equal(
            new InlineXbrlParser().Parse(Encoding.UTF8.GetString(source)).Select(FactIdentity),
            facts.Select(FactIdentity)
        );
    }

    [Theory]
    [InlineData("<title/>")]
    [InlineData("<title/><style/>")]
    [InlineData("<title/><script/>")]
    public void EmptyXhtmlHeadElementsDoNotHideIssuerPeriodOrFinancialFacts(string head)
    {
        var source = Encoding.UTF8.GetBytes(Report().Replace("<head>", "<head>" + head));
        var original = source.ToArray();
        var detected = EsefReportEnvelope.Discover(source, new(2025, 3, 1));
        Assert.Equal(Lei, detected.LegalEntityIdentifier);
        Assert.Equal(Period, detected.PeriodEnd);
        Assert.Equal(original, source);
        Assert.Equal(
            new InlineXbrlParser().Parse(Report()).Select(FactIdentity),
            new InlineXbrlParser()
                .Parse(Encoding.UTF8.GetString(detected.Envelope))
                .Select(FactIdentity)
        );
    }

    [Theory]
    [InlineData("\n", "\n")]
    [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n", "\r\n")]
    [InlineData(" \t\n<!-- Report -->\n", "\n<!-- End --> \t\n")]
    public void PreservesFactsWithWhitespaceOutsideDocumentElement(string prefix, string suffix)
    {
        var source = Encoding.UTF8.GetBytes(prefix + Report() + suffix);
        var original = source.ToArray();
        var detected = EsefReportEnvelope.Discover(source, new(2025, 3, 1));
        Assert.Equal(Lei, detected.LegalEntityIdentifier);
        Assert.Equal(Period, detected.PeriodEnd);
        Assert.Equal(original, source);
        Assert.Equal(EsefReportEnvelope.Build(source, Lei, Period), detected.Envelope);
        Assert.Equal(
            new InlineXbrlParser().Parse(Report()).Select(FactIdentity),
            new InlineXbrlParser()
                .Parse(Encoding.UTF8.GetString(detected.Envelope))
                .Select(FactIdentity)
        );
    }

    [Theory]
    [InlineData("unexpected", "")]
    [InlineData("", "unexpected")]
    public void RejectsNonWhitespaceOutsideDocumentElement(string prefix, string suffix) =>
        Assert.Throws<XmlException>(() =>
            EsefReportEnvelope.Discover(
                Encoding.UTF8.GetBytes(prefix + Report() + suffix),
                new(2025, 3, 1)
            )
        );

    [Fact]
    public void DetectsIssuerAndLatestAnnualPeriodFromComparativeFacts()
    {
        var source = WithAdditionalYear(2025);
        var detected = EsefReportEnvelope.Discover(Encoding.UTF8.GetBytes(source), new(2026, 3, 1));
        Assert.Equal(Lei, detected.LegalEntityIdentifier);
        Assert.Equal(new DateOnly(2025, 12, 31), detected.PeriodEnd);
        Assert.Equal(
            4,
            new InlineXbrlParser().Parse(Encoding.UTF8.GetString(detected.Envelope)).Count
        );
    }

    [Fact]
    public void RejectsMultipleConsolidatedIssuers()
    {
        var source = Report()
            .Replace(
                "id=\"c-3\"><xbrli:entity><xbrli:identifier scheme=\"http://standards.iso.org/iso/17442\">"
                    + Lei,
                "id=\"c-3\"><xbrli:entity><xbrli:identifier scheme=\"http://standards.iso.org/iso/17442\">5493001KJTIIGC8Y1R12"
            );
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Discover(Encoding.UTF8.GetBytes(source), new(2025, 3, 1))
        );
    }

    [Theory]
    [InlineData("529900G4A1IKOKC22K56", "529900G4A1IKOKC22K57")]
    [InlineData("http://standards.iso.org/iso/17442", "https://example.com/identifier")]
    [InlineData(
        "https://xbrl.ifrs.org/taxonomy/2024-03-27/ifrs-full",
        "https://example.com/ifrs-full"
    )]
    [InlineData("http://www.xbrl.org/2013/inlineXBRL", "https://example.com/inlineXBRL")]
    [InlineData("2024-01-01", "2024-07-01")]
    [InlineData("<xbrli:instant>2024-12-31", "<xbrli:instant>2023-12-31")]
    [InlineData("http://www.w3.org/1999/xhtml", "https://example.com/html")]
    public void DetectionRejectsUnsupportedEvidence(string before, string after) =>
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Discover(
                Encoding.UTF8.GetBytes(Report().Replace(before, after)),
                new(2025, 3, 1)
            )
        );

    [Fact]
    public void DetectionRejectsPeriodAfterPublication() =>
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Discover(Encoding.UTF8.GetBytes(Report()), new(2024, 12, 30))
        );

    [Fact]
    public void DetectionRejectsMissingPublication() =>
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Discover(Encoding.UTF8.GetBytes(Report()), default)
        );

    [Fact]
    public void DetectionRejectsLaterInterimInsteadOfChoosingOlderAnnual() =>
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Discover(
                Encoding.UTF8.GetBytes(
                    WithAdditionalYear(2025).Replace("2025-01-01", "2025-07-01")
                ),
                new(2026, 3, 1)
            )
        );

    [Theory]
    [InlineData(349, false)]
    [InlineData(350, true)]
    [InlineData(380, true)]
    [InlineData(381, false)]
    public void DiscoveryUsesExistingAnnualDurationBounds(int days, bool accepted)
    {
        var start = Period
            .AddDays(-days)
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var source = Encoding.UTF8.GetBytes(Report().Replace("2024-01-01", start));
        if (accepted)
            Assert.Equal(Period, EsefReportEnvelope.Discover(source, Period).PeriodEnd);
        else
            Assert.Throws<InvalidDataException>(() => EsefReportEnvelope.Discover(source, Period));
    }

    [Fact]
    public void DiscoveryPreservesOriginalBytesAndBuildEnvelope()
    {
        var source = Encoding.UTF8.GetBytes(Report());
        var original = source.ToArray();
        var report = EsefReportEnvelope.Discover(source, new(2025, 3, 1));
        Assert.Equal(original, source);
        Assert.Equal(EsefReportEnvelope.Build(source, Lei, Period), report.Envelope);
    }

    [Fact]
    public void DiscoveryRejectsQualifiedContexts() =>
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Discover(
                Encoding.UTF8.GetBytes(
                    Report()
                        .Replace(
                            "</xbrli:entity>",
                            "<xbrli:segment><xbrldi:explicitMember dimension='example:Axis'>example:Member</xbrldi:explicitMember></xbrli:segment></xbrli:entity>"
                        )
                ),
                new(2025, 3, 1)
            )
        );

    [Fact]
    public void DiscoveryRejectsExternalEntities() =>
        Assert.Throws<XmlException>(() =>
            EsefReportEnvelope.Discover(
                Encoding.UTF8.GetBytes(
                    "<!DOCTYPE html [<!ENTITY external SYSTEM 'file:///etc/passwd'>]>"
                        + Report().Replace("</body>", "&external;</body>")
                ),
                new(2025, 3, 1)
            )
        );

    [Theory]
    [InlineData("<!DOCTYPE html>")]
    [InlineData(
        "<!DOCTYPE html PUBLIC '-//W3C//DTD XHTML 1.0 Strict//EN' 'http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd'>"
    )]
    [InlineData("<!DOCTYPE html SYSTEM 'http://127.0.0.1:1/unreachable.dtd'>")]
    [InlineData("<!DOCTYPE html [<!ENTITY unused SYSTEM 'file:///nonexistent-esef-entity'>]>")]
    [InlineData(
        "<!DOCTYPE html [<!ENTITY % external SYSTEM 'http://127.0.0.1:1/external.dtd'>%external;]>"
    )]
    public void IgnoresDoctypeWithoutResolvingOrChangingFacts(string declaration)
    {
        var source = Encoding.UTF8.GetBytes(declaration + Report());
        var original = source.ToArray();
        var detected = EsefReportEnvelope.Discover(source, new(2025, 3, 1));
        Assert.Equal(Lei, detected.LegalEntityIdentifier);
        Assert.Equal(Period, detected.PeriodEnd);
        Assert.Equal(original, source);
        Assert.Equal(EsefReportEnvelope.Build(source, Lei, Period), detected.Envelope);
        Assert.DoesNotContain("<!DOCTYPE", Encoding.UTF8.GetString(detected.Envelope));
        Assert.Equal(
            new InlineXbrlParser().Parse(Report()).Select(FactIdentity),
            new InlineXbrlParser()
                .Parse(Encoding.UTF8.GetString(detected.Envelope))
                .Select(FactIdentity)
        );
    }

    [Theory]
    [InlineData("<!ENTITY injected '123'>")]
    [InlineData("<!ENTITY injected SYSTEM 'file:///etc/passwd'>")]
    [InlineData("<!ENTITY injected SYSTEM 'http://127.0.0.1:1/secret'>")]
    [InlineData("<!ENTITY first '123'><!ENTITY injected '&first;&first;&first;'>")]
    public void RejectsEntityReferencesInsteadOfExpandingThem(string declaration)
    {
        var source = Encoding.UTF8.GetBytes(
            "<!DOCTYPE html ["
                + declaration
                + "]>"
                + Report().Replace("</body>", "&injected;</body>")
        );
        Assert.Throws<XmlException>(() => EsefReportEnvelope.Discover(source, new(2025, 3, 1)));
        Assert.Throws<XmlException>(() => EsefReportEnvelope.Build(source, Lei, Period));
    }

    [Fact]
    public void DiscoveryAcceptsAlternateNamespacePrefixesWithoutChangingOriginalBytes()
    {
        var source = Report();
        foreach (
            var (original, alias) in new[]
            {
                ("ix", "inline"),
                ("xbrli", "instance"),
                ("ifrs-full", "financial"),
                ("iso4217", "currency"),
                ("ixt", "transform"),
            }
        )
            source = RenamePrefix(source, original, alias);
        var bytes = Encoding.UTF8.GetBytes(source);
        var discovered = EsefReportEnvelope.Discover(bytes, new(2025, 3, 1));
        Assert.Equal(source, Encoding.UTF8.GetString(bytes));
        Assert.Equal(Lei, discovered.LegalEntityIdentifier);
        Assert.Equal(Period, discovered.PeriodEnd);
        var facts = new InlineXbrlParser().Parse(Encoding.UTF8.GetString(discovered.Envelope));
        Assert.Equal(
            new InlineXbrlParser().Parse(Report()).Select(FactIdentity),
            facts.Select(FactIdentity)
        );
        var xml = XDocument.Parse(Encoding.UTF8.GetString(discovered.Envelope));
        XNamespace inline = "http://www.xbrl.org/2013/inlineXBRL";
        Assert.All(
            xml.Descendants(inline + "nonFraction"),
            element =>
            {
                var format = element.Attribute("format").Value.Split(':');
                Assert.Equal("num-comma-decimal", format[1]);
                Assert.Equal(
                    "http://www.xbrl.org/inlineXBRL/transformation/2022-02-16",
                    element.GetNamespaceOfPrefix(format[0])?.NamespaceName
                );
            }
        );
    }

    [Theory]
    [InlineData("https://example.com/instance")]
    [InlineData("http://www.xbrl.org/2003/instance-spoof")]
    public void RejectsSpoofedInstanceNamespaceForDiscoveryAndBuild(string namespaceUri)
    {
        var bytes = Encoding.UTF8.GetBytes(
            Report().Replace("http://www.xbrl.org/2003/instance", namespaceUri)
        );
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Discover(bytes, new(2025, 3, 1))
        );
        Assert.Throws<InvalidDataException>(() => EsefReportEnvelope.Build(bytes, Lei, Period));
    }

    [Fact]
    public void CanonicalizesQNameAttributesMeasuresAndExplicitMembers()
    {
        XNamespace instance = "http://www.xbrl.org/2003/instance";
        XNamespace dimensions = "http://xbrl.org/2006/xbrldi";
        XNamespace inline = "http://www.xbrl.org/2013/inlineXBRL";
        const string taxonomy = "https://xbrl.ifrs.org/taxonomy/2024-03-27/ifrs-full";
        var document = XDocument.Parse(Report());
        var context = new XElement(document.Descendants(instance + "context").First());
        context.SetAttributeValue("id", "qualified");
        context
            .Element(instance + "entity")
            .Add(
                new XElement(
                    instance + "segment",
                    new XElement(
                        dimensions + "explicitMember",
                        new XAttribute("dimension", "ifrs-full:ExampleAxis"),
                        "ifrs-full:ExampleMember"
                    )
                )
            );
        document.Descendants(inline + "resources").Single().Add(context);
        var fact = new XElement(document.Descendants(inline + "nonFraction").First());
        fact.SetAttributeValue("contextRef", "qualified");
        fact.SetAttributeValue("id", "qualified-fact");
        document.Root.Element(XName.Get("body", "http://www.w3.org/1999/xhtml")).Add(fact);
        var source = RenamePrefix(
            RenamePrefix(
                RenamePrefix(document.ToString(), "ifrs-full", "financial"),
                "xbrldi",
                "dim"
            ),
            "iso4217",
            "currency"
        );
        var result = EsefReportEnvelope.Discover(Encoding.UTF8.GetBytes(source), new(2025, 3, 1));
        var xml = XDocument.Parse(Encoding.UTF8.GetString(result.Envelope));
        Assert.All(
            xml.Descendants(inline + "nonFraction"),
            element =>
                Assert.Equal(
                    taxonomy,
                    element
                        .GetNamespaceOfPrefix(element.Attribute("name").Value.Split(':')[0])
                        ?.NamespaceName
                )
        );
        Assert.Equal("iso4217:EUR", xml.Descendants(instance + "measure").Single().Value);
        var member = xml.Descendants(dimensions + "explicitMember").Single();
        Assert.Equal("ifrs-full:ExampleAxis", member.Attribute("dimension").Value);
        Assert.Equal("ifrs-full:ExampleMember", member.Value);
        var parsed = new InlineXbrlParser().Parse(Encoding.UTF8.GetString(result.Envelope));
        var qualified = Assert.Single(parsed, candidate => candidate.Dimensions.Count != 0);
        Assert.Null(qualified.ConsolidatedLei);
        Assert.Equal("ifrs-full:ExampleAxis", qualified.Dimensions[0].Axis);
        Assert.Equal("ifrs-full:ExampleMember", qualified.Dimensions[0].Member);
    }

    [Fact]
    public void ScopedTaxonomyRebindingCannotTurnCustomFactsIntoStandardFacts()
    {
        var source = Report()
            .Replace(
                "name=\"ifrs-full:RevenueAndOperatingIncome\"",
                "xmlns:ifrs-full=\"https://example.com/custom\" name=\"ifrs-full:RevenueAndOperatingIncome\""
            );
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Discover(Encoding.UTF8.GetBytes(source), new(2025, 3, 1))
        );
    }

    [Fact]
    public void DiscoveryResolvesTaxonomyAliasDeclaredOnFactItself()
    {
        var source = Report()
            .Replace(
                "name=\"ifrs-full:RevenueAndOperatingIncome\"",
                "xmlns:local=\"https://xbrl.ifrs.org/taxonomy/2024-03-27/ifrs-full\" name=\"local:RevenueAndOperatingIncome\""
            );
        var result = EsefReportEnvelope.Discover(Encoding.UTF8.GetBytes(source), new(2025, 3, 1));
        Assert.Equal(Period, result.PeriodEnd);
        Assert.Contains(
            new InlineXbrlParser().Parse(Encoding.UTF8.GetString(result.Envelope)),
            fact => fact.Taxonomy == "ifrs-full" && fact.Tag == "RevenueAndOperatingIncome"
        );
    }

    [Fact]
    public void QualifiedContextsRemainQualifiedWithAlternatePrefixes()
    {
        var source = Report()
            .Replace(
                "</xbrli:entity>",
                "<xbrli:segment><xbrldi:explicitMember dimension='ifrs-full:Axis'>ifrs-full:Member</xbrldi:explicitMember></xbrli:segment></xbrli:entity>"
            );
        source = RenamePrefix(RenamePrefix(source, "xbrli", "instance"), "xbrldi", "dimensions");
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Discover(Encoding.UTF8.GetBytes(source), new(2025, 3, 1))
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesCanonicalCustomKpiTaxonomyAndDimensionKeys(bool scopedDeclaration)
    {
        XNamespace instance = "http://www.xbrl.org/2003/instance";
        XNamespace dimensions = "http://xbrl.org/2006/xbrldi";
        XNamespace inline = "http://www.xbrl.org/2013/inlineXBRL";
        const string taxonomy = "https://issuer.example/taxonomy/2024";
        var source = XDocument.Parse(Report());
        var context = new XElement(source.Descendants(instance + "context").First());
        context.SetAttributeValue("id", "custom-context");
        var member = new XElement(
            dimensions + "explicitMember",
            new XAttribute("dimension", "issuer:BusinessAxis"),
            "issuer:RetailMember"
        );
        context.Element(instance + "entity").Add(new XElement(instance + "segment", member));
        source.Descendants(inline + "resources").Single().Add(context);
        var fact = new XElement(source.Descendants(inline + "nonFraction").First());
        fact.SetAttributeValue("id", "custom-kpi");
        fact.SetAttributeValue("contextRef", "custom-context");
        fact.SetAttributeValue("name", "issuer:RetailAssets");
        source.Root.Element(XName.Get("body", "http://www.w3.org/1999/xhtml")).Add(fact);
        if (scopedDeclaration)
        {
            member.SetAttributeValue(XNamespace.Xmlns + "issuer", taxonomy);
            fact.SetAttributeValue(XNamespace.Xmlns + "issuer", taxonomy);
        }
        else
        {
            source.Root.SetAttributeValue(XNamespace.Xmlns + "issuer", taxonomy);
        }
        var original = source.ToString();
        var before = new InlineXbrlParser().Parse(original);
        var bytes = Encoding.UTF8.GetBytes(original);
        var discovered = EsefReportEnvelope.Discover(bytes, new(2025, 3, 1));
        var after = new InlineXbrlParser().Parse(Encoding.UTF8.GetString(discovered.Envelope));
        Assert.Equal(original, Encoding.UTF8.GetString(bytes));
        Assert.Equal(before.Select(FactIdentity), after.Select(FactIdentity));
        var custom = Assert.Single(after, candidate => candidate.Taxonomy == "issuer");
        Assert.Equal(taxonomy, custom.Namespace);
        Assert.Equal("RetailAssets", custom.Tag);
        Assert.Equal("issuer:BusinessAxis", Assert.Single(custom.Dimensions).Axis);
        Assert.Equal("issuer:RetailMember", custom.Dimensions[0].Member);
        var previous = Assert.Single(before, candidate => candidate.Taxonomy == "issuer");
        Assert.Equal(
            previous.Dimensions.Select(dimension => (dimension.Axis, dimension.Member)),
            custom.Dimensions.Select(dimension => (dimension.Axis, dimension.Member))
        );
    }

    [Fact]
    public void RejectsCustomPrefixRebindingInsteadOfPublishingUnderFirstNamespace()
    {
        var source = Report()
            .Replace("<body>", "<body xmlns:issuer='https://issuer.example/first'>")
            .Replace(
                "name=\"ifrs-full:Assets\"",
                "xmlns:issuer='https://issuer.example/second' name=\"issuer:Assets\""
            );
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Discover(Encoding.UTF8.GetBytes(source), new(2025, 3, 1))
        );
    }

    private static string RenamePrefix(string source, string original, string alias) =>
        source
            .Replace("xmlns:" + original + "=", "xmlns:" + alias + "=")
            .Replace(original + ":", alias + ":");

    private static string WithAdditionalYear(int year)
    {
        var additional = Report()
            .Replace("2024-", $"{year}-")
            .Replace("c-1", "next-1")
            .Replace("c-3", "next-3")
            .Replace("f-53", "next-53")
            .Replace("f-133", "next-133");
        var body = additional[
            (additional.IndexOf("<ix:header>", StringComparison.Ordinal))..additional.IndexOf(
                "</body>",
                StringComparison.Ordinal
            )
        ];
        return Report().Replace("</body>", body + "</body>");
    }

    [Fact]
    public void RetainsSourceNumbersContextsAndUnitsWithoutEmbeddedImages()
    {
        var source = Report();
        var before = new InlineXbrlParser().Parse(source);
        var compact = Encoding.UTF8.GetString(
            EsefReportEnvelope.Build(Encoding.UTF8.GetBytes(source), Lei, Period)
        );
        var after = new InlineXbrlParser().Parse(compact);
        Assert.DoesNotContain("base64", compact);
        Assert.Equal(before.Select(FactIdentity), after.Select(FactIdentity));
        Assert.Contains(
            after,
            fact => fact.Tag == "Assets" && fact.Value == 5708798762m && fact.Unit == "EUR"
        );
        Assert.Contains(
            after,
            fact => fact.Tag == "RevenueAndOperatingIncome" && fact.Value == 1107137972m
        );
    }

    [Fact]
    public void RetainsCompleteStylesheetOrderAndXmlEntitiesWithoutEmbeddedPayloads()
    {
        const string first =
            ".t { position:absolute; left:87px; bottom:166px; } .t > span { display:inline; }";
        const string second =
            ".t { left:92px !important; } @font-face { src:url('data:font/woff;base64,AAAA'); }";
        var xml = XDocument.Parse(Report(), LoadOptions.PreserveWhitespace);
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";
        var head = xml.Root.Element(xhtml + "head");
        head.RemoveNodes();
        head.Add(
            new XElement(xhtml + "style", new XAttribute("media", "screen"), first),
            new XElement(xhtml + "style", new XCData(second))
        );
        var source = Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
        var unchanged = source.ToArray();

        var envelope = EsefReportEnvelope.Discover(source, new(2025, 3, 1)).Envelope;

        var retained = XDocument
            .Parse(Encoding.UTF8.GetString(envelope))
            .Descendants(xhtml + "style")
            .ToArray();
        Assert.Equal(2, retained.Length);
        Assert.Equal(first, retained[0].Value);
        Assert.Equal("screen", retained[0].Attribute("media")?.Value);
        Assert.Equal(".t { left:92px !important; } @font-face { src:url(''); }", retained[1].Value);
        Assert.Equal(unchanged, source);
        Assert.Equal(
            new InlineXbrlParser().Parse(Report()).Select(FactIdentity),
            new InlineXbrlParser().Parse(Encoding.UTF8.GetString(envelope)).Select(FactIdentity)
        );
    }

    [Theory]
    [InlineData("529900G4A1IKOKC22K57", "2024-12-31")]
    [InlineData(Lei, "2025-12-31")]
    [InlineData(Lei, "2023-12-31")]
    public void RejectsIssuerAndPeriodMismatch(string lei, string period) =>
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Build(Encoding.UTF8.GetBytes(Report()), lei, DateOnly.Parse(period))
        );

    [Fact]
    public void RejectsInterimRatherThanLabellingItAnnual() =>
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Build(
                Encoding.UTF8.GetBytes(Report().Replace("2024-01-01", "2024-07-01")),
                Lei,
                Period
            )
        );

    [Fact]
    public void RejectsExternalEntities() =>
        Assert.Throws<XmlException>(() =>
            EsefReportEnvelope.Build(
                Encoding.UTF8.GetBytes(
                    "<!DOCTYPE html [<!ENTITY external SYSTEM 'file:///etc/passwd'>]>"
                        + Report().Replace("</body>", "&external;</body>")
                ),
                Lei,
                Period
            )
        );

    [Fact]
    public void NeverDiscardsFinancialMarkupInsideAStyleElement() =>
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Build(
                Encoding.UTF8.GetBytes(
                    Report().Replace("<style>", "<style><span>financial data</span>")
                ),
                Lei,
                Period
            )
        );

    [Fact]
    public void RejectsMissingConsolidatedEvidence() =>
        Assert.Throws<InvalidDataException>(() =>
            EsefReportEnvelope.Build(
                Encoding.UTF8.GetBytes(
                    Report()
                        .Replace(
                            "</xbrli:entity>",
                            "<xbrli:segment><xbrldi:explicitMember dimension='example:Axis'>example:Member</xbrldi:explicitMember></xbrli:segment></xbrli:entity>"
                        )
                ),
                Lei,
                Period
            )
        );

    private static string FactIdentity(
        Equibles.Sec.FinancialFacts.BusinessLogic.Models.ParsedXbrlFact fact
    ) =>
        $"{fact.ConsolidatedLei}/{fact.Namespace}/{fact.Tag}/{fact.Value}/{fact.Unit}/{fact.PeriodStart}/{fact.PeriodEnd}/{fact.Decimals}";

    internal static string Report() =>
        """
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:ix="http://www.xbrl.org/2013/inlineXBRL"
                  xmlns:xbrli="http://www.xbrl.org/2003/instance" xmlns:iso4217="http://www.xbrl.org/2003/iso4217"
                  xmlns:ifrs-full="https://xbrl.ifrs.org/taxonomy/2024-03-27/ifrs-full"
                  xmlns:xbrldi="http://xbrl.org/2006/xbrldi" xmlns:ixt="http://www.xbrl.org/inlineXBRL/transformation/2022-02-16">
            <head><style>span.image {background:url("data:image/png;base64,AAAA")}</style></head>
            <body><img src="data:image/png;base64,AAAA"/>
            <ix:header><ix:resources>
            <xbrli:context id="c-1"><xbrli:entity><xbrli:identifier scheme="http://standards.iso.org/iso/17442">529900G4A1IKOKC22K56</xbrli:identifier></xbrli:entity><xbrli:period><xbrli:instant>2024-12-31</xbrli:instant></xbrli:period></xbrli:context>
            <xbrli:context id="c-3"><xbrli:entity><xbrli:identifier scheme="http://standards.iso.org/iso/17442">529900G4A1IKOKC22K56</xbrli:identifier></xbrli:entity><xbrli:period><xbrli:startDate>2024-01-01</xbrli:startDate><xbrli:endDate>2024-12-31</xbrli:endDate></xbrli:period></xbrli:context>
            <xbrli:unit id="u-1"><xbrli:measure>iso4217:EUR</xbrli:measure></xbrli:unit>
            </ix:resources></ix:header>
            <ix:nonFraction unitRef="u-1" contextRef="c-1" decimals="0" name="ifrs-full:Assets" format="ixt:num-comma-decimal" scale="0" id="f-53">5 708 798 762</ix:nonFraction>
            <ix:nonFraction unitRef="u-1" contextRef="c-3" decimals="0" name="ifrs-full:RevenueAndOperatingIncome" format="ixt:num-comma-decimal" scale="0" id="f-133">1 107 137 972</ix:nonFraction>
            </body></html>
            """;
}
