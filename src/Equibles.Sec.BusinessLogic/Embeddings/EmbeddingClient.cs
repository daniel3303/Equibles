using System.Text.Json;
using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.BusinessLogic.Embeddings;

[Service(ServiceLifetime.Scoped, typeof(IEmbeddingClient))]
public class EmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _httpClient;
    private readonly EmbeddingConfig _config;
    private readonly ILogger<EmbeddingClient> _logger;

    public bool IsEnabled => _config.IsConfigured;

    public EmbeddingClient(
        IHttpClientFactory httpClientFactory,
        IOptions<EmbeddingConfig> config,
        ILogger<EmbeddingClient> logger
    )
    {
        _config = config.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();

        if (_config.IsConfigured)
        {
            _httpClient.BaseAddress = new Uri(_config.BaseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            if (!string.IsNullOrEmpty(_config.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.ApiKey}");
            }
        }
    }

    public async Task<float[]> GenerateEmbedding(string text)
    {
        var embeddings = await GenerateEmbeddings([text]);
        return embeddings.FirstOrDefault();
    }

    public async Task<List<float[]>> GenerateEmbeddings(List<string> texts)
    {
        if (!texts.Any() || !_config.IsConfigured)
        {
            return [];
        }

        var batches = texts.Chunk(_config.BatchSize).ToList();
        var allEmbeddings = new List<float[]>(texts.Count);

        foreach (var batch in batches)
        {
            allEmbeddings.AddRange(await ProcessBatch(batch.ToList()));
        }

        // Positionally aligned to `texts`; entries are null where embedding failed.
        return allEmbeddings;
    }

    public async Task<int> GetEmbeddingDimension()
    {
        try
        {
            var testEmbedding = await GenerateEmbedding("test");
            return testEmbedding?.Length ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting embedding dimension");
            throw;
        }
    }

    private async Task<List<float[]>> ProcessBatch(List<string> batch)
    {
        // Ollama's /api/embed handles one input per call. Embed each text
        // independently and return a list positionally aligned to `batch`,
        // using null for any text that fails (e.g. Ollama returns 500 because
        // the model emitted a NaN vector for that specific input). One bad
        // chunk must never abort the batch or crash the document processor —
        // the caller skips null entries so the backlog keeps draining.
        var embeddings = new List<float[]>(batch.Count);
        foreach (var text in batch)
        {
            try
            {
                var payload = new { model = _config.ModelName, input = text };

                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(
                    json,
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.PostAsync("/api/embed", content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OllamaEmbedResponse>(responseJson);

                embeddings.Add(
                    result?.Embeddings != null && result.Embeddings.Count > 0
                        ? result.Embeddings[0]
                        : null
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Skipping a chunk that failed to embed (continuing with the rest)"
                );
                embeddings.Add(null);
            }
        }

        return embeddings;
    }
}
