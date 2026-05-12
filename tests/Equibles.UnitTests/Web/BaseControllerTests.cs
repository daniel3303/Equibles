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

    private sealed class TestableBaseController : BaseController {
        public TestableBaseController() : base(Substitute.For<ILogger<BaseController>>()) { }

        public string InvokeGetReturnUrl() => GetReturnUrl();
    }
}
