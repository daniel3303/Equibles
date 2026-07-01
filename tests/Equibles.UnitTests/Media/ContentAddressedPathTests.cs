using System.Text;
using Equibles.Media.BusinessLogic.Storage;

namespace Equibles.UnitTests.Media;

public class ContentAddressedPathTests
{
    // The layout must be a pure function of the content hash with two levels of
    // 2-hex sharding: <tier>/sha256/<hash[0:2]>/<hash[2:4]>/<hash>. Any drift here
    // (extra segment, wrong shard width) breaks dedup or unbalances directories.
    [Fact]
    public void Build_ShardsBySha256Prefix_ProducesTwoLevelPath()
    {
        var hash = "a7f3c9d0e1b2a3f4c5d6e7f8091a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f90";

        var path = ContentAddressedPath.Build(FileStorageTiers.Blob, hash);

        path.Should().Be($"blob/sha256/a7/f3/{hash}");
    }

    // The audio tier lives under its own top-level segment so it can be mounted on a
    // separate disk; only the leading tier changes, the sharding is identical.
    [Fact]
    public void Build_AudioTier_UsesAudioTopLevelSegment()
    {
        var hash = "0011223344556677889900112233445566778899001122334455667788990011";

        var path = ContentAddressedPath.Build(FileStorageTiers.Audio, hash);

        path.Should().Be($"audio/sha256/00/11/{hash}");
    }

    // SHA-256 is the content address; pin the canonical NIST test vector so a hashing
    // regression (wrong algorithm, encoding, or casing) is caught immediately.
    [Fact]
    public void ComputeSha256Hex_KnownVector_MatchesLowercaseHex()
    {
        var hex = ContentAddressedPath.ComputeSha256Hex(Encoding.ASCII.GetBytes("abc"));

        hex.Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void Build_HashShorterThanShardWidth_Throws()
    {
        var act = () => ContentAddressedPath.Build(FileStorageTiers.Blob, "ab");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_MissingTier_Throws()
    {
        var act = () => ContentAddressedPath.Build("", "a7f3c9d0e1b2");

        act.Should().Throw<ArgumentException>();
    }
}
