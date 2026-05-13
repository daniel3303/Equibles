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
