using System.Text.Json;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Equibles.Web.Extensions;

public static class HtmlExtensions
{
    // Generic so the compile-time T matches what the inline call sites used,
    // keeping the JSON output byte-identical to the prior
    // @Html.Raw(JsonSerializer.Serialize(...)) pattern.
    public static IHtmlContent Json<T>(this IHtmlHelper html, T value) =>
        new HtmlString(JsonSerializer.Serialize(value));
}
