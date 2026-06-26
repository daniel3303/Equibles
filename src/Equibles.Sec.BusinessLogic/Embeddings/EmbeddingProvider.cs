using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.BusinessLogic.Embeddings;

// Which embedding backend the Embedding/EmbeddingModel config section points at.
public enum EmbeddingProvider
{
    // Self-hosted Ollama server (/api/embed) — the GPU-free, easy-to-run default.
    [Display(Name = "Ollama")]
    Ollama,

    // Any OpenAI-compatible embeddings server (/v1/embeddings) — vLLM, Text-Embeddings-Inference,
    // or OpenAI itself. Use this for high-throughput batched serving in production.
    [Display(Name = "OpenAI")]
    OpenAI,
}
