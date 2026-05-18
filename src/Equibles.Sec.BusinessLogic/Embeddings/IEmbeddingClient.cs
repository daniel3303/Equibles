namespace Equibles.Sec.BusinessLogic.Embeddings;

public interface IEmbeddingClient
{
    bool IsEnabled { get; }

    /// <summary>
    /// Embeds a single text. Returns <c>null</c> when the text could not be
    /// embedded (e.g. the model emitted a degenerate vector) — callers must
    /// null-check the result.
    /// </summary>
    Task<float[]> GenerateEmbedding(string text);

    /// <summary>
    /// Embeds each text. The result is positionally aligned to
    /// <paramref name="texts"/> (same length, same order); an entry is
    /// <c>null</c> where that text could not be embedded. Callers must keep the
    /// alignment and null-check each entry. A single failed text never aborts
    /// the batch.
    /// </summary>
    Task<List<float[]>> GenerateEmbeddings(List<string> texts);

    Task<int> GetEmbeddingDimension();
}
