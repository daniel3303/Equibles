using System.Text.Json;
using System.Text.Json.Serialization;
using Equibles.Media.HostedService.Models;

namespace Equibles.Media.HostedService;

/// <summary>
/// Keeps the sweep's progress in the store root, so a worker restart neither starts a new run
/// inside the interval nor walks an interrupted run from its first folder again. Every sweep
/// step is idempotent, so a lost or unreadable state costs only a repeated walk.
/// </summary>
internal class BlobSweepStateStore
{
    internal const string FileName = ".blob-sweep-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger _logger;

    public BlobSweepStateStore(string root, ILogger logger)
    {
        _path = Path.Combine(root, FileName);
        _logger = logger;
    }

    public BlobSweepState Load()
    {
        if (!System.IO.File.Exists(_path))
        {
            return new BlobSweepState();
        }

        try
        {
            return JsonSerializer.Deserialize<BlobSweepState>(
                    System.IO.File.ReadAllText(_path),
                    JsonOptions
                ) ?? new BlobSweepState();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(
                ex,
                "Blob sweep state {Path} is unreadable; the sweep starts a fresh run",
                _path
            );
            return new BlobSweepState();
        }
    }

    // The rename replaces the previous state whole, so a crash mid-save leaves the old one.
    public void Save(BlobSweepState state)
    {
        var temp = _path + ".tmp";
        System.IO.File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
        System.IO.File.Move(temp, _path, overwrite: true);
    }
}
