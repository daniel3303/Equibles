namespace Equibles.Sec.BusinessLogic.Embeddings;

public class EmbeddingConfig
{
    public bool Enabled { get; set; }

    // Which API the embedding server speaks. Ollama (/api/embed) by default; OpenAI (/v1/embeddings)
    // for a batched server like vLLM or TEI.
    public EmbeddingProvider Provider { get; set; }

    public string ModelName { get; set; }
    public string BaseUrl { get; set; }
    public string ApiKey { get; set; }
    public int BatchSize { get; set; } = 10;

    public bool IsConfigured =>
        Enabled && !string.IsNullOrEmpty(BaseUrl) && !string.IsNullOrEmpty(ModelName);
}
