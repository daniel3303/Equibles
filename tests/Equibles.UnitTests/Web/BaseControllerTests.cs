using Equibles.Web.Controllers.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class BaseControllerTests {
    [Fact]
    public void GetReturnUrl_ExternalUrlInQueryString_IsRejectedAndReturnsNull() {
        // GetReturnUrl is shared by every concrete controller that wants to honour a
        // ?ReturnUrl= round-trip — most importantly the Auth flow's post-login redirect.
        // The `Url.IsLocalUrl(returnUrl)` filter is the only thing standing between this
        // and an open-redirect that an attacker can use to land an authenticated victim
        // on a phishing page. The risk this test pins: a refactor that "simplifies" the
        // filter (e.g. drops the IsLocalUrl check and trusts the input, or replaces it
        // with a naive `StartsWith("/")` that absolute URLs like `//evil.com/phish` slip
        // past) would silently re-enable open-redirect on every controller that uses
        // GetReturnUrl. The companion `AuthController_*WithNonLocalReturnUrl` test only
        // covers the concrete Auth POST path; this one pins the base-helper directly so
        // any other controller adopting GetReturnUrl is protected.
        var sut = new TestableBaseController();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.QueryString = new QueryString("?ReturnUrl=https%3A%2F%2Fevil.com%2Fphish");

        var urlHelper = Substitute.For<IUrlHelper>();
        urlHelper.IsLocalUrl(Arg.Any<string>()).Returns(callInfo => {
            var url = callInfo.Arg<string>();
            return !string.IsNullOrEmpty(url) && url.StartsWith("/") && !url.StartsWith("//");
        });

        sut.ControllerContext = new ControllerContext {
            HttpContext = httpContext,
        };
        sut.Url = urlHelper;

        var result = sut.InvokeGetReturnUrl();

        result.Should().BeNull();
    }

    [Fact]
    public void GetReturnUrl_LocalUrlPostedInForm_ReturnsTheUrl() {
        // GetReturnUrl checks the FORM body first (`Request.HasFormContentType &&
        // Request.Form.ContainsKey("ReturnUrl")`) before falling back to the query
        // string. The login flow relies on this: the login view renders the
        // ReturnUrl as a hidden `<input>` and submits it via POST, so the
        // form-body branch is the load-bearing path for "send me back where I
        // was after authenticating." The existing GetReturnUrl_External*
        // test only exercises the query-string-rejection branch — it leaves the
        // form-body short-circuit AND the success-return branch (`return returnUrl;`)
        // unverified. A refactor that drops the form check (or that flips the if/else
        // chain order so the form branch is unreachable when a query string is also
        // present) would silently route every post-login redirect through the
        // fallback `RedirectToAction("Index","Home")`, breaking deep links into
        // protected pages with no failure signal. Pin BOTH that the form branch
        // runs AND that a confirmed-local URL is returned verbatim.
        var sut = new TestableBaseController();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues> {
            ["ReturnUrl"] = "/Stocks/Details/42",
        });

        var urlHelper = Substitute.For<IUrlHelper>();
        urlHelper.IsLocalUrl(Arg.Any<string>()).Returns(callInfo => {
            var url = callInfo.Arg<string>();
            return !string.IsNullOrEmpty(url) && url.StartsWith("/") && !url.StartsWith("//");
        });

        sut.ControllerContext = new ControllerContext { HttpContext = httpContext };
        sut.Url = urlHelper;

        var result = sut.InvokeGetReturnUrl();

        result.Should().Be("/Stocks/Details/42");
    }

    [Fact]
    public void InitSseStream_SetsContentTypeAndDisablesNginxBuffering() {
        // InitSseStream prepares a response for Server-Sent Events. The three
        // headers it sets are load-bearing: text/event-stream is the SSE
        // content type, no-cache prevents downstream caches from holding
        // the stream, and X-Accel-Buffering: no tells nginx (and other
        // reverse proxies that honour the header) NOT to buffer the
        // response — without it, the proxy holds bytes until the
        // connection closes and SSE clients see nothing until then. Pin
        // all three so a refactor that "simplifies" the helper to a
        // single ContentType set can't silently break streaming.
        var sut = new TestableBaseController();
        var httpContext = new DefaultHttpContext();
        sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

        sut.InvokeInitSseStream();

        httpContext.Response.ContentType.Should().Be("text/event-stream");
        httpContext.Response.Headers.CacheControl.ToString().Should().Be("no-cache");
        httpContext.Response.Headers["X-Accel-Buffering"].ToString().Should().Be("no");
    }

    [Fact]
    public async Task WriteSseEvent_FormatsEventLineWithDoubleNewlineTerminatorAndJsonPayload() {
        // Sibling to the InitSseStream pin above. The two helpers form a contract:
        // InitSseStream prepares the response headers, WriteSseEvent writes
        // individual events. The existing test pins the headers; this one pins
        // the actual event format, which has equally narrow SSE-protocol
        // requirements.
        //
        // The event format emitted is `event: <type>\ndata: <json>\n\n`. THREE
        // separate parts are load-bearing:
        //   1. The `event: <type>` line names the event so JS clients can register
        //      `addEventListener('<type>', handler)` against it.
        //   2. The `data: <json>` line carries the payload — JsonConvert-serialized
        //      so JS clients can `JSON.parse(event.data)` without quirks (e.g.
        //      System.Text.Json would emit camelCase keys, breaking the contract).
        //   3. The TRAILING `\n\n` (TWO newlines, not one) is what the SSE protocol
        //      uses as the event delimiter. Drop one newline and the event never
        //      flushes from the client's perspective — the spec specifies that an
        //      event "is dispatched" only when a blank line is encountered. A
        //      regression to a single `\n` terminator makes every SSE feed silently
        //      hang: bytes reach the wire, but no event handler ever fires until
        //      the NEXT event arrives (or the connection closes).
        //
        // Critically, the FlushAsync after the write ensures the bytes leave the
        // Kestrel response buffer immediately rather than waiting for the buffer
        // to fill. Without that flush, every event would accumulate in memory and
        // a long-lived SSE stream would deliver events in unpredictable batches
        // tied to buffer fill timing — utterly defeating the real-time
        // requirement that SSE exists for. The flush is implicitly tested here:
        // a `MemoryStream` Response.Body would still see the bytes after Write
        // even WITHOUT the flush (in-memory streams have no buffer to flush past),
        // but the explicit FlushAsync call is the load-bearing wire-level guarantee
        // for real Kestrel responses. We pin the format and trust the FlushAsync
        // to remain.
        //
        // The risk this catches:
        //   - Regression dropping one `\n` from the terminator → silent SSE hangs
        //   - Regression to `data: ... \n event: ...` order swap → standards
        //     violation, breaks `addEventListener` registration in clients
        //   - Regression swapping JsonConvert for System.Text.Json → camelCase
        //     keys break every existing client's JSON.parse field lookup
        var sut = new TestableBaseController();
        var httpContext = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;
        sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var payload = new { CompaniesProcessed = 42, Message = "Halfway done" };
        await sut.InvokeWriteSseEvent("progress", payload);

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody);
        var written = await reader.ReadToEndAsync();

        written.Should().Be("event: progress\ndata: {\"CompaniesProcessed\":42,\"Message\":\"Halfway done\"}\n\n");
    }

    private sealed class TestableBaseController : BaseController {
        public TestableBaseController() : base(Substitute.For<ILogger<BaseController>>()) { }

        public string InvokeGetReturnUrl() => GetReturnUrl();

        public void InvokeInitSseStream() => InitSseStream();

        public Task InvokeWriteSseEvent(string eventType, object data) => WriteSseEvent(eventType, data);
    }
}
