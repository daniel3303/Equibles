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
            _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, _config.RequestTimeoutSeconds));

            if (!string.IsNullOrEmpty(_config.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.ApiKey}");
            }
        }
    }

    public async Task<float[]> GenerateEmbedding(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        var embeddings = await GenerateEmbeddings([text], cancellationToken);
        return embeddings.FirstOrDefault();
    }

    public async Task<List<float[]>> GenerateEmbeddings(
        List<string> texts,
        CancellationToken cancellationToken = default
    )
    {
        if (!texts.Any() || !_config.IsConfigured)
        {
            return [];
        }

        var batches = texts.Chunk(_config.BatchSize).ToList();
        var allEmbeddings = new List<float[]>(texts.Count);

        foreach (var batch in batches)
        {
            allEmbeddings.AddRange(await ProcessBatch(batch.ToList(), cancellationToken));
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

    private async Task<List<float[]>> ProcessBatch(
        List<string> batch,
        CancellationToken cancellationToken
    )
    {
        // OpenAI-compatible servers (vLLM/TEI) embed a whole ARRAY of inputs in one batched forward
        // pass — dramatically faster than one request per text (which makes vLLM process them
        // serially). So send the batch as a single array request. Ollama's /api/embed takes one
        // input per call, so there we fan out concurrently instead (BatchSize = concurrency width).
        // Either way the result is positionally aligned to `batch`; an entry is null where that text
        // failed. One bad chunk must never abort the batch — the caller skips null entries so the
        // backlog keeps draining.
        if (_config.Provider == EmbeddingProvider.OpenAI)
        {
            var batched = await TryEmbedBatchViaOpenAi(batch, cancellationToken);
            if (batched != null)
                return batched;
            // The whole-array request failed (transient, or one poison input rejected the array) —
            // fall through to per-text so a single bad chunk can't drop the rest of the batch.
        }

        var embeddings = await Task.WhenAll(
            batch.Select(text => EmbedSingle(text, cancellationToken))
        );

        // One aggregated line per batch instead of one warning per failed chunk: under a saturated
        // or restarting server every chunk fails, and per-chunk warnings flooded the log by the
        // hundreds of thousands, burying real errors. Per-chunk detail stays at Debug.
        var failed = embeddings.Count(e => e == null);
        if (failed > 0)
        {
            _logger.LogWarning(
                "Skipped {Failed} of {Total} chunks that failed to embed (continuing with the rest)",
                failed,
                embeddings.Length
            );
        }

        return embeddings.ToList();
    }

    // Embeds the whole batch in ONE /v1/embeddings request (vLLM/TEI batch the array). Returns the
    // vectors positionally aligned to `batch`, or null if the request failed so the caller can fall
    // back to per-text. Never throws.
    private async Task<List<float[]>> TryEmbedBatchViaOpenAi(
        List<string> batch,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var payload = new { model = _config.ModelName, input = batch };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(
                json,
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(
                "/v1/embeddings",
                content,
                cancellationToken
            );
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<OpenAiEmbedResponse>(responseJson);
            if (result?.Data == null || result.Data.Count != batch.Count)
                return null;

            // The OpenAI shape returns one entry per input with an `index` mapping it back to its
            // input position — order by that rather than trusting array order.
            var ordered = new float[batch.Count][];
            foreach (var item in result.Data)
                if (item.Index >= 0 && item.Index < ordered.Length)
                    ordered[item.Index] = item.Embedding;
            return [.. ordered];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch embedding request failed; falling back to per-text");
            return null;
        }
    }

    private async Task<float[]> EmbedSingle(string text, CancellationToken cancellationToken)
    {
        try
        {
            // Same { model, input } payload either way; only the endpoint and response shape differ.
            var payload = new { model = _config.ModelName, input = text };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(
                json,
                System.Text.Encoding.UTF8,
                "application/json"
            );

            return _config.Provider == EmbeddingProvider.OpenAI
                ? await EmbedViaOpenAi(content, cancellationToken)
                : await EmbedViaOllama(content, cancellationToken);
        }
        catch (Exception ex)
        {
            // Per-chunk failures are aggregated into one warning per batch by ProcessBatch.
            _logger.LogDebug(ex, "Chunk failed to embed (continuing with the rest)");
            return null;
        }
    }

    private async Task<float[]> EmbedViaOllama(
        StringContent content,
        CancellationToken cancellationToken
    )
    {
        var response = await _httpClient.PostAsync("/api/embed", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<OllamaEmbedResponse>(responseJson);

        return result?.Embeddings is { Count: > 0 } ? result.Embeddings[0] : null;
    }

    private async Task<float[]> EmbedViaOpenAi(
        StringContent content,
        CancellationToken cancellationToken
    )
    {
        var response = await _httpClient.PostAsync("/v1/embeddings", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<OpenAiEmbedResponse>(responseJson);

        return result?.Data is { Count: > 0 } ? result.Data[0].Embedding : null;
    }
}
