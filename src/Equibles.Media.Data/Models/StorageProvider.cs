using System.Collections.Concurrent;

namespace Equibles.Media.Data.Models;

/// <summary>
/// Where a <see cref="File"/>'s bytes physically live. Stored as a string in the
/// database via a ValueConverter (see MediaModuleConfiguration), following the
/// DocumentType/ErrorSource smart-enum convention. Extensible via <see cref="Register"/>
/// so a deployment can add backends (e.g. object storage) without changing this class.
/// </summary>
public sealed class StorageProvider
{
    public string Value { get; }

    public StorageProvider(string value)
    {
        Value = value;
    }

    /// <summary>Bytes stored inline in the FileContent table — the original behavior.</summary>
    public static readonly StorageProvider Database = new("Database");

    /// <summary>Bytes stored on a content-addressed filesystem tree; the File carries RelativePath + ContentHash.</summary>
    public static readonly StorageProvider FileSystem = new("FileSystem");

    private static readonly ConcurrentDictionary<string, StorageProvider> AllByValue = new(
        new[]
        {
            new KeyValuePair<string, StorageProvider>(Database.Value, Database),
            new KeyValuePair<string, StorageProvider>(FileSystem.Value, FileSystem),
        },
        StringComparer.OrdinalIgnoreCase
    );

    public static StorageProvider FromValue(string value)
    {
        // The OrdinalIgnoreCase comparer's GetHashCode throws on null, so a null
        // lookup must short-circuit. FromValue returns null on no match — the EF
        // value converter relies on this for NULL columns.
        if (value == null)
        {
            return null;
        }

        return AllByValue.GetValueOrDefault(value);
    }

    public static IEnumerable<StorageProvider> GetAll() => AllByValue.Values;

    public static void Register(StorageProvider provider)
    {
        AllByValue.TryAdd(provider.Value, provider);
    }

    public override string ToString() => Value;

    public override bool Equals(object obj) => obj is StorageProvider other && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(StorageProvider left, StorageProvider right) =>
        Equals(left, right);

    public static bool operator !=(StorageProvider left, StorageProvider right) =>
        !Equals(left, right);
}
