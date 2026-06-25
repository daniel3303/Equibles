using System.Text;
using Equibles.Media.BusinessLogic;
using Newtonsoft.Json;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>
/// Packs the captured as-reported artifacts — <c>FilingSummary.xml</c> plus the statement
/// <c>R#.htm</c> tables — into the single gzip blob stored on
/// <c>Document.ReportedStatementsContent</c>, and unpacks it for the local parse step. The blob
/// is a JSON map of filename → file text, so the parse step is fully offline (no EDGAR re-fetch)
/// and re-runnable when the parser version is bumped.
/// </summary>
public static class ReportedStatementsBundle
{
    /// <summary>Serializes the files to JSON and gzip-compresses them; returns the blob and its pre-compression size.</summary>
    public static (byte[] Compressed, long UncompressedSize) Pack(
        IReadOnlyDictionary<string, string> files
    )
    {
        var raw = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(files));
        return (GzipCompressor.Compress(raw), raw.Length);
    }

    /// <summary>Reverses <see cref="Pack"/>: decompresses the blob back to its filename → file-text map.</summary>
    public static Dictionary<string, string> Unpack(byte[] compressed)
    {
        var raw = Encoding.UTF8.GetString(GzipCompressor.Decompress(compressed));
        return JsonConvert.DeserializeObject<Dictionary<string, string>>(raw) ?? [];
    }
}
