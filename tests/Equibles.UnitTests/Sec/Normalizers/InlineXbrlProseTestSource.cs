using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

internal static class InlineXbrlProseTestSource
{
    internal const string Start = "Glaston Corporation's financing agreement con-";
    internal const string End = "sists of EUR 32 million loans and a EUR 25 million facility.";

    internal static string Source(string first = Start, string second = End, string between = "") =>
        "<html xmlns:ix='http://www.xbrl.org/2013/inlineXBRL'><body>"
        + "<ix:nonNumeric name='ifrs-full:Liquidity' contextRef='c1' escape='true' continuedAt='next'>"
        + $"<p>{first}</p></ix:nonNumeric>{between}"
        + $"<ix:continuation id='next'><div><p>{second}</p></div></ix:continuation></body></html>";

    internal static IHtmlDocument Convert(string source)
    {
        var document = new HtmlParser(
            new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
        ).ParseDocument(source);
        new InlineXbrlProseConversionStep().Execute(document);
        return document;
    }
}
