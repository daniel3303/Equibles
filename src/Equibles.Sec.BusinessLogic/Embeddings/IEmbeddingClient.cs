namespace Equibles.Sec.BusinessLogic.Embeddings;

public interface IEmbeddingClient {
    bool IsEnabled { get; }
    Task<float[]> GenerateEmbedding(string text);
    Task<List<float[]>> GenerateEmbeddings(List<string> texts);
    Task<int> GetEmbeddingDimension();
}