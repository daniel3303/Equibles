using Newtonsoft.Json.Linq;

namespace Equibles.Integrations.Yahoo.Models.Responses;

/// <summary>
/// Volume arrays. Same contract as <see cref="TolerantDecimalListConverter"/>: an impossible
/// magnitude degrades to null (read downstream as "no volume reported") instead of costing the
/// listing its whole response.
/// </summary>
public class TolerantLongListConverter : TolerantNumberListConverter<long>
{
    protected override long? Convert(JToken element)
    {
        var value = AsDouble(element);
        if (
            value == null
            || !double.IsFinite(value.Value)
            || value.Value > long.MaxValue
            || value.Value < long.MinValue
        )
            return null;
        return (long)value.Value;
    }
}
