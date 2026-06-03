using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientReadDecodedBodyGzipTests
{
    // Contract (from the method's own doc): SEC's S3-backed Archives gzip error
    // bodies even with no Accept-Encoding, and the encoding token may be the
    // alternate spelling "x-gzip" (case-insensitive per RFC 9110). ReadDecodedBody
    // must therefore decode an x-gzip body back to its original text — if the loose
    // match regressed to exact "gzip", the body would slip through undecoded and be
    // misread as binary garbage. We attack that exact branch.
    [Fact]
    public async Task ReadDecodedBody_XGzipEncodedBody_DecompressesToOriginalText()
    {
        const string original = "No daily index published for this date.";
        var gzipped = Gzip(original);

        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
        {
            Content = new ByteArrayContent(gzipped),
        };
        // Alternate spelling + uppercase to exercise the case-insensitive loose match.
        response.Content.Headers.ContentEncoding.Add("X-GZIP");

        var method = typeof(SecEdgarClient).GetMethod(
            "ReadDecodedBody",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var task = (Task<string>)method!.Invoke(null, [response, CancellationToken.None]);
        var decoded = await task;

        decoded.Should().Be(original);
    }

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }
}
