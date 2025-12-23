using System.Text.Json.Serialization;

namespace Equibles.Sec.BusinessLogic.Embeddings;

internal class OllamaEmbedResponse {
    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("embeddings")]
    public List<float[]> Embeddings { get; set; }
}
