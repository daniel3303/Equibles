using Newtonsoft.Json.Linq;

namespace Equibles.Integrations.Yahoo.Models.Responses;

/// <summary>
/// Price arrays. A magnitude outside <see cref="decimal"/> becomes null rather than aborting
/// the response — see <see cref="TolerantNumberListConverter{T}"/> for why that matters.
/// </summary>
public class TolerantDecimalListConverter : TolerantNumberListConverter<decimal>
{
    // decimal tops out around 7.9E28. Compared as a double because that is what the reader
    // hands us; the bound is deliberately exclusive-safe rather than exact, since a value
    // within rounding distance of the ceiling is not a real price either.
    private const double MaxDecimal = 7.9e28;

    protected override decimal? Convert(JToken element)
    {
        var value = AsDouble(element);
        if (value == null || !double.IsFinite(value.Value) || Math.Abs(value.Value) > MaxDecimal)
            return null;
        return (decimal)value.Value;
    }
}
