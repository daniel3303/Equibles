using System.Text.Json;
using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.BusinessLogic.Embeddings;

[Service(ServiceLifetime.Scoped, typeof(IEmbeddingClient))]
public class EmbeddingClient : IEmbeddingClient {
    private readonly HttpClient _httpClient;
    private readonly EmbeddingConfig _config;
    private readonly ILogger<EmbeddingClient> _logger;

    public bool IsEnabled => _config.IsConfigured;

    public EmbeddingClient(IHttpClientFactory httpClientFactory, IOptions<EmbeddingConfig> config,
        ILogger<EmbeddingClient> logger
    ) {
        _config = config.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();

        if (_config.IsConfigured) {
            _httpClient.BaseAddress = new Uri(_config.BaseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            if (!string.IsNullOrEmpty(_config.ApiKey)) {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.ApiKey}");
            }
        }
    }

    public async Task<float[]> GenerateEmbedding(string text) {
        var embeddings = await GenerateEmbeddings([text]);
        return embeddings.FirstOrDefault();
    }

    public async Task<List<float[]>> GenerateEmbeddings(List<string> texts) {
        if (!texts.Any() || !_config.IsConfigured) {
            return [];
        }

        try {
            var batches = texts.Chunk(_config.BatchSize).ToList();
            var allEmbeddings = new List<float[]>();

            foreach (var batch in batches) {
                var batchEmbeddings = await ProcessBatch(batch.ToList());
                allEmbeddings.AddRange(batchEmbeddings);
            }

            return allEmbeddings;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error generating embeddings for {Count} texts", texts.Count);
            throw;
        }
    }

    public async Task<int> GetEmbeddingDimension() {
        try {
            var testEmbedding = await GenerateEmbedding("test");
            return testEmbedding?.Length ?? 0;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error getting embedding dimension");
            throw;
        }
    }

    private async Task<List<float[]>> ProcessBatch(List<string> batch) {
        // For single text, we can call the new /api/embed endpoint
        if (batch.Count == 1) {
            var singlePayload = new {
                model = _config.ModelName,
                input = batch[0]
            };

            var singleJson = JsonSerializer.Serialize(singlePayload);
            var singleContent = new StringContent(singleJson, System.Text.Encoding.UTF8, "application/json");

            // Use the new /api/embed endpoint
            var response = await _httpClient.PostAsync("/api/embed", singleContent);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OllamaEmbedResponse>(responseJson);

            return result.Embeddings;
        }

        // For multiple texts, we need to call the endpoint multiple times
        // as Ollama's /api/embed doesn't support batch processing like OpenAI
        var embeddings = new List<float[]>();
        foreach (var text in batch) {
            var payload = new {
                model = _config.ModelName,
                input = text
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/api/embed", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OllamaEmbedResponse>(responseJson);

            if (result.Embeddings != null && result.Embeddings.Count > 0) {
                embeddings.Add(result.Embeddings[0]);
            }
        }

        return embeddings;
    }
}