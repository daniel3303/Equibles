using System.Text.Json.Serialization;

namespace Equibles.Sec.BusinessLogic.Embeddings;

// Response shape for the OpenAI-compatible /v1/embeddings endpoint (vLLM, TEI, OpenAI):
// { "data": [ { "index": 0, "embedding": [..] } ], "model": "..", "usage": {..} }. One entry per
// input; `index` maps each vector back to its input position. model/usage are ignored.
internal class OpenAiEmbedResponse
{
    [JsonPropertyName("data")]
    public List<OpenAiEmbedData> Data { get; set; }
}

internal class OpenAiEmbedData
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; }
}
