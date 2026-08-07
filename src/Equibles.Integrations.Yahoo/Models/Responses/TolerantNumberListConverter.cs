using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Equibles.Integrations.Yahoo.Models.Responses;

/// <summary>
/// Reads a chart price/volume array, mapping any element the CLR type cannot represent to
/// null instead of throwing.
///
/// The feed occasionally publishes a nonsense magnitude for a single field of a single
/// listing — an <c>adjclose</c> of <c>1.2036464262466904E35</c> was served for one ADR every
/// pass from 2026-08-05. That exceeds <see cref="decimal.MaxValue"/> (~7.9E28), so the default
/// binding threw mid-parse and Newtonsoft abandoned the WHOLE chart response: the listing lost
/// its open/high/low/close/volume too, though every one of those was valid, and it re-failed
/// identically on every later cycle because the upstream value never self-corrects.
///
/// A null element already means "unavailable" everywhere downstream — the row builder treats
/// a missing adjusted close as a fall-back to the day's close, and rejects a row whose OHLC
/// is incomplete. So degrading one impossible field to null keeps the rest of the response,
/// and keeps a single poisoned value from costing a listing its entire history.
/// </summary>
public abstract class TolerantNumberListConverter<T> : JsonConverter<List<T?>>
    where T : struct
{
    public override List<T?> ReadJson(
        JsonReader reader,
        Type objectType,
        List<T?> existingValue,
        bool hasExistingValue,
        JsonSerializer serializer
    )
    {
        var token = JToken.Load(reader);
        if (token.Type != JTokenType.Array)
            return [];

        var values = new List<T?>();
        foreach (var element in token.Children())
            values.Add(Convert(element));
        return values;
    }

    public override void WriteJson(JsonWriter writer, List<T?> value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        foreach (var item in value ?? [])
        {
            if (item.HasValue)
                writer.WriteValue(item.Value);
            else
                writer.WriteNull();
        }
        writer.WriteEndArray();
    }

    /// <summary>
    /// The element's value, or null when it is absent, non-numeric, or outside what
    /// <typeparamref name="T"/> can hold.
    /// </summary>
    protected abstract T? Convert(JToken element);

    /// <summary>
    /// The element read as a double — the widest type the JSON reader parses a float into, so
    /// an out-of-range magnitude arrives here as a finite-or-infinite double rather than an
    /// exception. Null for a JSON null, a non-numeric token, or an unparseable string.
    /// </summary>
    protected static double? AsDouble(JToken element)
    {
        switch (element.Type)
        {
            case JTokenType.Integer:
            case JTokenType.Float:
                try
                {
                    return element.Value<double>();
                }
                catch (OverflowException)
                {
                    // An integer literal too large even for double (the feed has never sent
                    // one, but the array is attacker-shaped data from our side of the wire).
                    return null;
                }
            case JTokenType.String:
                return double.TryParse(
                    (string)element,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed
                )
                    ? parsed
                    : null;
            default:
                return null;
        }
    }
}
