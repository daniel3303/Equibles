using System.Globalization;
using System.IO.Compression;
using Equibles.Integrations.DelayedTrades;

namespace Equibles.Integrations.Euronext.DelayedTrades;

// Streams the one CSV entry of a trades zip row by row; a Paris session is close to a million rows and is never
// held in memory as a whole.
public static class EuronextTradesFileParser
{
    public const string EntryName = "Trades_Equities.csv";
    public const int MaxInflatedBytes = 256 * 1024 * 1024;
    public const int MaxMalformedRows = 100;

    public static readonly string[] Header =
    [
        "TradingDateTime",
        "PublicationDateTime",
        "MifidInstrumentID",
        "MifidPrice",
        "MifidQuantity",
        "MifidPriceNotation",
        "MifidCurrency",
        "MmtMarketMechanism",
        "MmtNegotiationIndicator",
        "MmtModificationIndicator",
        "MmtBenchMarkIndicator",
        "MmtContributionToPrice",
        "MmtAlgorithmicIndicator",
        "MmtPublicationMode",
        "Venue",
        "ThirdCountryTradingVenueExecution",
        "TradeUniqueIdentifier",
        "MissingPrice",
        "MmtContingentTransactionIndicator",
        "VenueOfPublication",
    ];

    public static IEnumerable<DelayedTradePrint> Read(
        byte[] zipBytes,
        DelayedTradeParseCounters counters
    )
    {
        ArgumentNullException.ThrowIfNull(zipBytes);
        ArgumentNullException.ThrowIfNull(counters);
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        if (archive.Entries.Count != 1 || archive.Entries[0].FullName != EntryName)
            throw new InvalidDataException(
                $"A Euronext trades file holds exactly one {EntryName} entry."
            );
        using var reader = new StreamReader(
            new BoundedReadStream(archive.Entries[0].Open(), MaxInflatedBytes),
            System.Text.Encoding.UTF8
        );
        var notice = reader.ReadLine();
        if (
            string.IsNullOrWhiteSpace(notice)
            || notice.StartsWith(Header[0], StringComparison.Ordinal)
        )
            throw new InvalidDataException(
                "The trades file no longer opens with its terms notice."
            );
        var header = EuronextTradesCsv.Split(reader.ReadLine(), Header.Length);
        if (header == null || !header.SequenceEqual(Header, StringComparer.Ordinal))
            throw new InvalidDataException("The trades file header changed shape.");
        var lineNumber = 2;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (line.Length == 0)
                continue;
            counters.Rows++;
            var print = ReadRow(line, lineNumber);
            if (print == null)
            {
                counters.MalformedRows++;
                if (counters.MalformedRows > MaxMalformedRows)
                    throw new InvalidDataException(
                        $"More than {MaxMalformedRows} malformed rows; the trades file changed shape."
                    );
                continue;
            }
            yield return print;
        }
    }

    public static DelayedTradePrint ReadRow(string line, int lineNumber)
    {
        var fields = EuronextTradesCsv.Split(line, Header.Length);
        if (fields == null)
            return null;
        if (
            !TryReadUtc(fields[0], out var tradedAt)
            || !TryReadUtc(fields[1], out var publishedAt)
            || !decimal.TryParse(
                fields[3],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var price
            )
            || !decimal.TryParse(
                fields[4],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var quantity
            )
            || !TryReadModification(fields[9], out var modification)
        )
            return null;
        var isin = fields[2].Trim();
        var venue = fields[14].Trim();
        if (isin.Length != 12 || venue.Length != 4)
            return null;
        return new DelayedTradePrint(
            isin,
            venue,
            tradedAt,
            publishedAt,
            price,
            quantity,
            fields[6].Trim(),
            fields[5].Trim(),
            modification,
            fields[16].Trim(),
            fields[7].Trim(),
            fields[10].Trim(),
            fields[11].Trim(),
            fields[8].Trim(),
            IsFlagged(fields[17]),
            lineNumber
        );
    }

    // A blank or dash flag is the venue's "not applicable"; anything else names a missing-price reason.
    private static bool IsFlagged(string token) =>
        !string.IsNullOrWhiteSpace(token) && token.Trim() != "-";

    private static bool TryReadModification(string token, out DelayedTradeModification modification)
    {
        switch (token.Trim())
        {
            case "":
            case "-":
                modification = DelayedTradeModification.None;
                return true;
            case "AMND":
                modification = DelayedTradeModification.Amendment;
                return true;
            case "CANC":
                modification = DelayedTradeModification.Cancellation;
                return true;
            default:
                modification = default;
                return false;
        }
    }

    private static bool TryReadUtc(string token, out DateTime utc)
    {
        if (
            DateTimeOffset.TryParse(
                token,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed
            )
            && parsed.Offset == TimeSpan.Zero
        )
        {
            utc = parsed.UtcDateTime;
            return true;
        }
        utc = default;
        return false;
    }

    // Counts inflated bytes as they pass so a zip bomb stops at the cap instead of filling memory.
    private sealed class BoundedReadStream(Stream inner, long limit) : Stream
    {
        private long _read;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            _read += read;
            if (_read > limit)
                throw new InvalidDataException("The trades file exceeds the inflated size cap.");
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
