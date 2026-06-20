using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Equibles.Integrations.GovernmentContracts.Models;

/// <summary>
/// Reads a USAspending classification field (NAICS, PSC) that the API returns either as a
/// bare string (<c>"561210"</c>) or as an object (<c>{ "code": "561210", "description": "..." }</c>).
/// Always yields the bare code string (or null), so downstream mapping stays shape-agnostic.
/// </summary>
public class UsaSpendingCodeConverter : JsonConverter<string>
{
    public override string ReadJson(
        JsonReader reader,
        Type objectType,
        string existingValue,
        bool hasExistingValue,
        JsonSerializer serializer
    )
    {
        var token = JToken.Load(reader);
        return token.Type switch
        {
            JTokenType.Null => null,
            JTokenType.Object => (string)token["code"],
            JTokenType.String => (string)token,
            _ => token.ToString(),
        };
    }

    public override void WriteJson(JsonWriter writer, string value, JsonSerializer serializer) =>
        writer.WriteValue(value);
}
