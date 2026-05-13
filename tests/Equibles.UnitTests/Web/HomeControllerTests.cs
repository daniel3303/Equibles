using Equibles.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class HomeControllerTests {
    [Fact]
    public void Error_StatusCode429_ShowsTooManyRequestsTitleAndSets429Response() {
        // Pins the 429 case-specific branch — distinct from 404 and the default
        // arm pinned by sibling tests. 429 ("Too Many Requests") is the response
        // ASP.NET Core surfaces when the configured rate limiter rejects a
        // request, and the title users see on that page is the only signal that
        // distinguishes "you're being throttled" from "the page is broken". An
        // operator inspecting logs/dashboards relies on the title to triage:
        // a 429 surfaced with "Something Went Wrong" looks like a server crash
        // and triggers wrong incident response.
        //
        // The risk this test pins: same shape as the 404 sibling — a refactor
        // that swaps `429 => "Too Many Requests"` with another arm (or that
        // collapses the case into the default) would silently rebrand
        // rate-limit pages. The compiler doesn't care about case-label values,
        // and the integration tests don't render the error pipeline against
        // throttled requests, so this regression escapes other tiers entirely.
        var httpContext = new DefaultHttpContext();
        var controller = new HomeController(NullLogger<HomeController>.Instance, Substitute.For<IConfiguration>()) {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = Substitute.For<ITempDataDictionary>(),
        };

        var result = controller.Error(statusCode: 429);

        result.Should().BeOfType<ViewResult>();
        httpContext.Response.StatusCode.Should().Be(429);
        controller.ViewData["Title"].Should().Be("Too Many Requests");
    }

    [Fact]
    public void Error_StatusCode404_ShowsPageNotFoundTitleAndSets404Response() {
        // The sibling `Error_NullStatusCode_...` test pins the default switch arm
        // ("Something Went Wrong"). This pins the 404 case — the most common
        // user-facing error and the one with the strongest UX/SEO consequences:
        //
        //   * Users see the title in the browser tab and on the page header. A
        //     404 page rendering "Too Many Requests" or "Something Went Wrong"
        //     looks broken.
        //   * Search engines crawling a 404 page index the title. Mislabelling
        //     it pollutes search results for the site.
        //   * Monitoring tools log titles alongside status codes when capturing
        //     error pages — a 404 with the wrong title pattern would skew
        //     dashboards built on title-substring grouping.
        //
        // The risk: a refactor that simplifies the switch by collapsing arms
        // ("404 and 429 both go to default") or that swaps two case labels in a
        // copy-paste would shop a 404 with "Too Many Requests" or "Something
        // Went Wrong" as the title. Both the response status (404) and the
        // resolved title ("Page Not Found") are asserted on the same call —
        // collapsing them into a single Theory wouldn't isolate this case
        // distinctly enough to catch a swap.
        var httpContext = new DefaultHttpContext();
        var controller = new HomeController(NullLogger<HomeController>.Instance, Substitute.For<IConfiguration>()) {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = Substitute.For<ITempDataDictionary>(),
        };

        var result = controller.Error(statusCode: 404);

        result.Should().BeOfType<ViewResult>();
        httpContext.Response.StatusCode.Should().Be(404);
        controller.ViewData["Title"].Should().Be("Page Not Found");
    }

    [Fact]
    public void Error_NullStatusCode_SetsResponseStatusTo500AndShowsGenericTitle() {
        // ASP.NET Core's `UseStatusCodePagesWithReExecute("/Home/Error/{0}")` invokes
        // this action with the status code embedded in the URL — but the catch-all
        // exception handler invokes it WITHOUT a parameter (it doesn't know the
        // numeric code). The `?? 500` fallback is what guarantees that an
        // unhandled-exception page still responds with HTTP 500 instead of 200 OK,
        // and that the user sees the generic "Something Went Wrong" title rather
        // than a blank or 404-flavoured message.
        //
        // The risk this test pins: a refactor that drops the `?? 500` coalesce
        // (e.g. typing the parameter as plain `int` with default 0, or to a
        // `default(int?)` ToString) would cause the unhandled-exception path to
        // emit a 200 OK with status-code 0. Monitoring tools (Sentry, Datadog,
        // synthetic checks) that distinguish 5xx from 2xx would silently lose
        // visibility into every uncaught exception. Pin both the response status
        // and the default-arm switch output on a single null-input call.
        var httpContext = new DefaultHttpContext();
        var controller = new HomeController(NullLogger<HomeController>.Instance, Substitute.For<IConfiguration>()) {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = Substitute.For<ITempDataDictionary>(),
        };

        var result = controller.Error(statusCode: null);

        result.Should().BeOfType<ViewResult>();
        httpContext.Response.StatusCode.Should().Be(500);
        controller.ViewData["Title"].Should().Be("Something Went Wrong");
    }
}
