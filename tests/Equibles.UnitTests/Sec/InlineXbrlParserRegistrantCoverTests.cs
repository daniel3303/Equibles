using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the registrant cover facts ParseEnvelope reads: the SEC file number and the
/// shell-company flag, from the filer's own unqualified CIK context only. The boolean decode is
/// format-driven: filers render any glyph under a fixed-value transformation (production covers
/// draw a crossed box under ixt:fixed-false), so the glyph is read only for the SEC ballot-box
/// transformation and never guessed for anything else.
/// </summary>
public class InlineXbrlParserRegistrantCoverTests
{
    private const string Cik = "0001287032";

    private const string DocOpen =
        "<html "
        + "xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:ixt=\"http://www.xbrl.org/inlineXBRL/transformation/2020-02-12\" "
        + "xmlns:ixt-sec=\"http://www.sec.gov/inlineXBRL/transformation/2015-08-31\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xbrldi=\"http://xbrl.org/2006/xbrldi\" "
        + "xmlns:dei=\"http://xbrl.sec.gov/dei/2026\" "
        + "xmlns:acme=\"http://www.acme.example/20260630\""
        + "><body>"
        + "<ix:header><ix:resources>"
        + "<xbrli:context id=\"c-1\"><xbrli:entity><xbrli:identifier scheme=\"http://www.sec.gov/CIK\">"
        + Cik
        + "</xbrli:identifier></xbrli:entity>"
        + "<xbrli:period><xbrli:startDate>2025-07-01</xbrli:startDate><xbrli:endDate>2026-06-30</xbrli:endDate></xbrli:period></xbrli:context>"
        + "<xbrli:context id=\"c-sub\"><xbrli:entity><xbrli:identifier scheme=\"http://www.sec.gov/CIK\">"
        + Cik
        + "</xbrli:identifier><xbrli:segment><xbrldi:explicitMember dimension=\"dei:LegalEntityAxis\">acme:SubsidiaryMember</xbrldi:explicitMember></xbrli:segment></xbrli:entity>"
        + "<xbrli:period><xbrli:startDate>2025-07-01</xbrli:startDate><xbrli:endDate>2026-06-30</xbrli:endDate></xbrli:period></xbrli:context>"
        + "</ix:resources></ix:header>";

    private const string DocClose = "</body></html>";

    [Fact]
    public void ParseEnvelope_BdcCoverUnderFixedFalse_ReadsFileNumberAndIgnoresTheCrossedGlyph()
    {
        // The production shape of a business development company's 10-K: an 814- file number and
        // a crossed box drawn under ixt:fixed-false.
        var html =
            DocOpen
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityFileNumber\" id=\"f-6\">814-00659</ix:nonNumeric>"
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityShellCompany\" format=\"ixt:fixed-false\" id=\"f-31\">&#9746;</ix:nonNumeric>"
            + DocClose;

        var cover = new InlineXbrlParser()
            .ParseEnvelope(html)
            .RegistrantCovers.Should()
            .ContainSingle()
            .Subject;

        cover.Cik.Should().Be(Cik);
        cover.FileNumber.Should().Be("814-00659");
        cover.IsShellCompany.Should().BeFalse();
    }

    [Fact]
    public void ParseEnvelope_FileNumberSplitAcrossMarkup_ReadsTheWholeNumber()
    {
        var html =
            DocOpen
            + "<ix:nonNumeric name=\"dei:EntityFileNumber\" contextRef=\"c-1\"><b>814</b>-<b>00802</b></ix:nonNumeric>"
            + DocClose;

        Single(html).FileNumber.Should().Be("814-00802");
    }

    [Theory]
    [InlineData("ixt:fixed-true", "&#9746;", true)]
    [InlineData("ixt:fixed-true", "x", true)]
    [InlineData("ixt:fixed-false", "&#254;", false)]
    [InlineData("ixt:fixed-false", "o", false)]
    [InlineData("ixt:booleantrue", "Yes", true)]
    [InlineData("ixt:booleanfalse", "No", false)]
    [InlineData("ixt-sec:boolballotbox", "&#9746;", true)]
    [InlineData("ixt-sec:boolballotbox", "&#9745;", true)]
    [InlineData("ixt-sec:boolballotbox", "&#9744;", false)]
    [InlineData("ixt-sec:boolballotbox", "<span>&#9744;</span>", false)]
    public void ParseEnvelope_ShellFlagUnderATransformation_DecodesFromTheFormat(
        string format,
        string rendering,
        bool expected
    )
    {
        var html =
            DocOpen
            + $"<ix:nonNumeric contextRef=\"c-1\" format=\"{format}\" name=\"dei:EntityShellCompany\">{rendering}</ix:nonNumeric>"
            + DocClose;

        Single(html).IsShellCompany.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "false", false)]
    [InlineData(null, "true", true)]
    [InlineData(null, "TRUE", true)]
    public void ParseEnvelope_UntransformedShellFlag_ReadsTheLiteralWord(
        string format,
        string rendering,
        bool expected
    )
    {
        var html =
            DocOpen
            + $"<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityShellCompany\"{FormatAttribute(format)}>{rendering}</ix:nonNumeric>"
            + DocClose;

        Single(html).IsShellCompany.Should().Be(expected);
    }

    [Theory]
    // A glyph with no transformation is not a boolean.
    [InlineData(null, "&#9746;")]
    // A ballot box transformation over anything but a box.
    [InlineData("ixt-sec:boolballotbox", "Yes")]
    // A transformation that is not a boolean at all.
    [InlineData("ixt:num-dot-decimal", "1")]
    // A prefix bound to no transformation registry.
    [InlineData("acme:fixed-true", "x")]
    public void ParseEnvelope_UndecodableShellFlag_IsUnknownNotFalse(
        string format,
        string rendering
    )
    {
        var html =
            DocOpen
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityFileNumber\">001-36364</ix:nonNumeric>"
            + $"<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityShellCompany\"{FormatAttribute(format)}>{rendering}</ix:nonNumeric>"
            + DocClose;

        var cover = Single(html);

        cover.FileNumber.Should().Be("001-36364");
        cover.IsShellCompany.Should().BeNull();
    }

    [Fact]
    public void ParseEnvelope_TwoDifferentValuesForOneRegistrant_LeavesEachUnknown()
    {
        var html =
            DocOpen
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityFileNumber\">814-00659</ix:nonNumeric>"
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityFileNumber\">001-33901</ix:nonNumeric>"
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityShellCompany\" format=\"ixt:fixed-false\">x</ix:nonNumeric>"
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityShellCompany\" format=\"ixt:fixed-true\">x</ix:nonNumeric>"
            + DocClose;

        var cover = Single(html);

        cover.FileNumber.Should().BeNull();
        cover.IsShellCompany.Should().BeNull();
    }

    [Fact]
    public void ParseEnvelope_UndecodableRenderingBesideADecodableOne_LeavesTheFlagUnknown()
    {
        var html =
            DocOpen
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityShellCompany\" format=\"ixt:fixed-false\">x</ix:nonNumeric>"
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityShellCompany\">&#9746;</ix:nonNumeric>"
            + DocClose;

        Single(html).IsShellCompany.Should().BeNull();
    }

    [Fact]
    public void ParseEnvelope_RepeatedIdenticalRendering_KeepsTheValue()
    {
        var html =
            DocOpen
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityFileNumber\">001-42306</ix:nonNumeric>"
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityFileNumber\"> 001-42306 </ix:nonNumeric>"
            + DocClose;

        Single(html).FileNumber.Should().Be("001-42306");
    }

    [Fact]
    public void ParseEnvelope_CoRegistrantDimensionedContext_NeverAnswersForTheFiler()
    {
        var html =
            DocOpen
            + "<ix:nonNumeric contextRef=\"c-sub\" name=\"dei:EntityFileNumber\">814-00001</ix:nonNumeric>"
            + "<ix:nonNumeric contextRef=\"c-sub\" name=\"dei:EntityShellCompany\" format=\"ixt:fixed-true\">x</ix:nonNumeric>"
            + DocClose;

        new InlineXbrlParser().ParseEnvelope(html).RegistrantCovers.Should().BeEmpty();
    }

    [Fact]
    public void ParseEnvelope_SameLocalNameInAFilerExtension_IsIgnored()
    {
        var html =
            DocOpen
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"acme:EntityFileNumber\">814-00001</ix:nonNumeric>"
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"acme:EntityShellCompany\">true</ix:nonNumeric>"
            + DocClose;

        new InlineXbrlParser().ParseEnvelope(html).RegistrantCovers.Should().BeEmpty();
    }

    [Fact]
    public void ParseEnvelope_NilFacts_AreNotStatements()
    {
        var html =
            DocOpen
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityFileNumber\" xsi:nil=\"true\"></ix:nonNumeric>"
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityShellCompany\" xsi:nil=\"true\">false</ix:nonNumeric>"
            + DocClose;

        new InlineXbrlParser().ParseEnvelope(html).RegistrantCovers.Should().BeEmpty();
    }

    [Fact]
    public void ParseEnvelope_CoverWithoutRegistrantFacts_YieldsNothing()
    {
        var html =
            DocOpen
            + "<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityRegistrantName\">Acme Corp</ix:nonNumeric>"
            + DocClose;

        new InlineXbrlParser().ParseEnvelope(html).RegistrantCovers.Should().BeEmpty();
    }

    private static Equibles.Sec.FinancialFacts.BusinessLogic.Models.ParsedRegistrantCover Single(
        string html
    ) =>
        new InlineXbrlParser()
            .ParseEnvelope(html)
            .RegistrantCovers.Should()
            .ContainSingle()
            .Subject;

    private static string FormatAttribute(string format) =>
        format == null ? "" : $" format=\"{format}\"";
}
