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

    // A continuous-batching server (vLLM) queues requests under backfill load, so a whole-array
    // request can legitimately wait well beyond the old 30s HttpClient default before its forward
    // pass starts. A too-tight timeout aborts the batch, and the per-text fallback then times out
    // the same way — wasting the server's work and flooding the log with per-chunk failures.
    public int RequestTimeoutSeconds { get; set; } = 120;

    public bool IsConfigured =>
        Enabled && !string.IsNullOrEmpty(BaseUrl) && !string.IsNullOrEmpty(ModelName);
}
