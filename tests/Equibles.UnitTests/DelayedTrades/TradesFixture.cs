using System.IO.Compression;
using System.Security.Cryptography;
using Equibles.Integrations.DelayedTrades;
using Equibles.Integrations.Euronext.DelayedTrades;

namespace Equibles.UnitTests.DelayedTrades;

// The committed fixture is the CSV; the served file is a zip with one entry, so tests zip it the same way.
internal static class TradesFixture
{
    public const string LisbonExcerpt = "Trades_Equities.lisbon-excerpt.csv";
    public const string HeaderOnly = "Trades_Equities.header-only.csv";
    public static readonly DateTime FetchedAt = new(2026, 9, 16, 3, 54, 32, DateTimeKind.Utc);

    public static string Csv(string name) =>
        System.IO.File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Euronext", "Trades", name)
        );

    public static byte[] Zip(string csv, string entryName = EuronextTradesFileParser.EntryName)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry(entryName).Open());
            writer.Write(csv);
        }
        return stream.ToArray();
    }

    public static DelayedTradeFile Served(
        byte[] zip,
        DelayedTradeWindow window = DelayedTradeWindow.PreviousSession,
        DateTime? fetchedAt = null,
        string location = "LIS"
    ) =>
        new(
            EuronextDelayedTradeTerms.SourceKey,
            location,
            window,
            DelayedTradeFetchOutcome.Served,
            EuronextDelayedTradeSource.FileUrl(location, window).ToString(),
            EuronextDelayedTradeTerms.TermsUrl,
            Convert.ToHexStringLower(SHA256.HashData(zip)),
            zip.Length,
            fetchedAt ?? FetchedAt,
            zip
        );

    public static DelayedTradeFile Lisbon(
        DelayedTradeWindow window = DelayedTradeWindow.PreviousSession,
        DateTime? fetchedAt = null
    ) => Served(Zip(Csv(LisbonExcerpt)), window, fetchedAt);
}
