using System.IO.Compression;
using Equibles.Core.AutoWiring;

namespace Equibles.Holdings.HostedService.Services;

[Service]
public class TsvParser {
    public async IAsyncEnumerable<Dictionary<string, string>> ParseEntry(ZipArchiveEntry entry) {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        var headerLine = await reader.ReadLineAsync();
        if (headerLine == null) yield break;

        var headers = headerLine.Split('\t');

        while (await reader.ReadLineAsync() is { } line) {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var values = line.Split('\t');
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < headers.Length && i < values.Length; i++) {
                row[headers[i].Trim()] = values[i].Trim();
            }

            yield return row;
        }
    }
}
