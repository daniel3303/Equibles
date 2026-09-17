using System.Net;

namespace Equibles.UnitTests.Esef;

// Answers the index and the report addresses the index states, and nothing else: an address the service
// composed rather than took from the index shows up here as an unexpected request.
internal sealed class EsefIndexTestHandler(IReadOnlyDictionary<string, string> bodies)
    : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    // States a Content-Length larger than the body actually served, which is what tells a header pre-check
    // apart from a cap applied while reading: only the pre-check can refuse a body this small.
    public long? OverstatedContentLength { get; set; }

    // The paths that overstated length applies to. Empty means every path; naming one lets a report be
    // refused while the index it was read from stays readable.
    public HashSet<string> OverstatedPaths { get; } = [];

    // Answers without a Content-Length at all, which is what the real host does: it serves the report
    // chunked, so a ceiling can only be reached by reading the body.
    public bool OmitContentLength { get; set; }

    // How many bytes of the last body the caller actually pulled, so a refusal that still paid for the
    // whole download is visible.
    public long BodyBytesRead => _body?.BytesRead ?? 0;

    private CountingStream _body;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(request.RequestUri);
        var key = request.RequestUri.PathAndQuery;
        if (!bodies.TryGetValue(key, out var body))
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request }
            );
        HttpContent content;
        if (OmitContentLength)
        {
            // A non-seekable stream cannot state its length, which is exactly the shape the real host has.
            _body = new CountingStream(System.Text.Encoding.UTF8.GetBytes(body));
            content = new StreamContent(_body);
        }
        else
        {
            content = new StringContent(body);
            if (
                OverstatedContentLength != null
                && (OverstatedPaths.Count == 0 || OverstatedPaths.Contains(key))
            )
                content.Headers.ContentLength = OverstatedContentLength;
        }
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
                RequestMessage = request,
            }
        );
    }

    private sealed class CountingStream(byte[] payload) : Stream
    {
        private int _position;

        public long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = Math.Min(count, payload.Length - _position);
            Array.Copy(payload, _position, buffer, offset, take);
            _position += take;
            BytesRead += take;
            return take;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
