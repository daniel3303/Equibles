using Equibles.Media.Data.Models;

namespace Equibles.UnitTests.Media;

public class StorageProviderTests
{
    // The EF value converter round-trips through FromValue, so a known value must map
    // back to the canonical singleton (case-insensitively, matching the DB collation).
    [Theory]
    [InlineData("Database")]
    [InlineData("database")]
    [InlineData("FileSystem")]
    [InlineData("filesystem")]
    public void FromValue_KnownValue_ReturnsCanonicalInstance(string value)
    {
        var provider = StorageProvider.FromValue(value);

        provider.Should().NotBeNull();
        provider.Value.Should().BeOneOf("Database", "FileSystem");
    }

    // The converter relies on FromValue returning null for NULL/unknown columns so it
    // can fall back (FromValue(v) ?? new StorageProvider(v)) — a null lookup must not throw.
    [Fact]
    public void FromValue_Null_ReturnsNull()
    {
        StorageProvider.FromValue(null).Should().BeNull();
    }

    [Fact]
    public void FromValue_Unknown_ReturnsNull()
    {
        StorageProvider.FromValue("ObjectStore").Should().BeNull();
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        (StorageProvider.Database == StorageProvider.FromValue("Database")).Should().BeTrue();
        (StorageProvider.Database != StorageProvider.FileSystem).Should().BeTrue();
    }
}
