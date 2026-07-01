using System.Security.Cryptography;

namespace Equibles.Media.BusinessLogic.Storage;

/// <summary>
/// Builds the content-addressed relative path for a blob and computes its hash.
/// Layout: <c>&lt;tier&gt;/sha256/&lt;hash[0:2]&gt;/&lt;hash[2:4]&gt;/&lt;hash&gt;</c> — two levels of
/// 2-hex sharding keep any directory to a few hundred entries even at tens of millions
/// of files. The path is a pure function of the content, so identical bytes always map
/// to the same location (free deduplication). Paths use forward slashes so the stored
/// RelativePath is OS-independent.
/// </summary>
public static class ContentAddressedPath
{
    public const string Algorithm = "sha256";

    /// <summary>Prefix for the stored <c>File.ContentHash</c>, e.g. "sha256:" — multihash-style so a future algorithm change can coexist.</summary>
    public const string HashPrefix = Algorithm + ":";

    public static string ComputeSha256Hex(byte[] content)
    {
        return Convert.ToHexStringLower(SHA256.HashData(content));
    }

    public static string Build(string tier, string hashHex)
    {
        if (string.IsNullOrEmpty(tier))
        {
            throw new ArgumentException("Tier is required.", nameof(tier));
        }

        if (hashHex == null || hashHex.Length < 4)
        {
            throw new ArgumentException("Hash must be at least 4 hex characters.", nameof(hashHex));
        }

        return $"{tier}/{Algorithm}/{hashHex[..2]}/{hashHex[2..4]}/{hashHex}";
    }

    /// <summary>Converts a stored forward-slash relative path to the host OS separator.</summary>
    public static string ToOsPath(string relativePath)
    {
        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }
}
