using Equibles.Media.BusinessLogic;

namespace Equibles.UnitTests.Media;

// Lane B (coverage): GzipCompressor.Compress is otherwise unexercised. Its
// contract is that a payload compressed here decompresses back byte-for-byte
// (capture and replay share this codec), so pin the round-trip over a buffer
// spanning every byte value 0-255 to guard binary fidelity against regression.
public class GzipCompressorRoundTripTests
{
    [Fact]
    public void Compress_ThenDecompress_RestoresEveryByteValue()
    {
        var original = new byte[256 * 4];
        for (var i = 0; i < original.Length; i++)
            original[i] = (byte)(i % 256);

        var restored = GzipCompressor.Decompress(GzipCompressor.Compress(original));

        restored.Should().Equal(original);
    }
}
