using System.Security.Claims;
using Equibles.Web.Authentication;
using Equibles.Web.Controllers;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.FlashMessage.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.Tests.Web;

public class AuthControllerTests {
    private const string ValidUsername = "admin";
    private const string ValidPassword = "secret123";
    private const string SessionSecret = "test-session-secret";

    private readonly IFlashMessage _flashMessage;
    private readonly ILogger<BaseController> _logger;

    public AuthControllerTests() {
        _flashMessage = Substitute.For<IFlashMessage>();
        _logger = Substitute.For<ILogger<BaseController>>();
    }

    private AuthController CreateController(
        AuthSettings authSettings,
        ClaimsPrincipal? user = null,
        bool isHttps = false) {
        var options = Options.Create(authSettings);
        var controller = new AuthController(_logger, options, _flashMessage);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = isHttps ? "https" : "http";

        if (user != null) {
            httpContext.User = user;
        }

        var tempData = Substitute.For<ITempDataDictionary>();

        var urlHelper = Substitute.For<IUrlHelper>();
        urlHelper.IsLocalUrl(Arg.Any<string>()).Returns(callInfo => {
            var url = callInfo.Arg<string>();
            return !string.IsNullOrEmpty(url) && url.StartsWith("/");
        });

        controller.ControllerContext = new ControllerContext {
            HttpContext = httpContext
        };
        controller.TempData = tempData;
        controller.Url = urlHelper;

        return controller;
    }

    private static AuthSettings EnabledAuthSettings() => new() {
        Username = ValidUsername,
        Password = ValidPassword,
        SessionSecret = SessionSecret
    };

    private static AuthSettings DisabledAuthSettings() => new() {
        Username = null!,
        Password = null!
    };

    private static ClaimsPrincipal AuthenticatedUser(string username = ValidUsername) {
        var claims = new[] { new Claim(ClaimTypes.Name, username) };
        var identity = new ClaimsIdentity(claims, EnvAuthHandler.SchemeName);
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal AnonymousUser() {
        var claims = new[] { new Claim(ClaimTypes.Name, EnvAuthHandler.AnonymousUsername) };
        var identity = new ClaimsIdentity(claims, EnvAuthHandler.SchemeName);
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal UnauthenticatedUser() {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    // ── Login GET ──────────────────────────────────────────────────────

    [Fact]
    public void LoginGet_AuthEnabled_UnauthenticatedUser_ReturnsView() {
        var controller = CreateController(EnabledAuthSettings(), UnauthenticatedUser());

        var result = controller.Login(returnUrl: null!);

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public void LoginGet_AuthEnabled_SetsReturnUrlInViewData() {
        var controller = CreateController(EnabledAuthSettings(), UnauthenticatedUser());

        controller.Login(returnUrl: "/stocks");

        controller.ViewData["ReturnUrl"].Should().Be("/stocks");
    }

    [Fact]
    public void LoginGet_AuthEnabled_AnonymousUser_ReturnsView() {
        var controller = CreateController(EnabledAuthSettings(), AnonymousUser());

        var result = controller.Login(returnUrl: null!);

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public void LoginGet_AuthEnabled_AuthenticatedUser_RedirectsToHome() {
        var controller = CreateController(EnabledAuthSettings(), AuthenticatedUser());

        var result = controller.Login(returnUrl: null!);

        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be("Index");
        redirect.ControllerName.Should().Be("Home");
    }

    [Fact]
    public void LoginGet_AuthDisabled_RedirectsToHome() {
        var controller = CreateController(DisabledAuthSettings());

        var result = controller.Login(returnUrl: null!);

        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be("Index");
        redirect.ControllerName.Should().Be("Home");
    }

    // ── Login POST ─────────────────────────────────────────────────────

    [Fact]
    public void LoginPost_ValidCredentials_RedirectsToHome() {
        var controller = CreateController(EnabledAuthSettings());

        var result = controller.Login(ValidUsername, ValidPassword, returnUrl: null!);

        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be("Index");
        redirect.ControllerName.Should().Be("Home");
    }

    [Fact]
    public void LoginPost_ValidCredentials_SetsCookie() {
        var controller = CreateController(EnabledAuthSettings());

        controller.Login(ValidUsername, ValidPassword, returnUrl: null!);

        var cookies = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
        cookies.Should().Contain(EnvAuthHandler.SchemeName);
    }

    [Fact]
    public void LoginPost_ValidCredentials_CookieContainsExpectedToken() {
        var controller = CreateController(EnabledAuthSettings());
        var expectedToken = EnvAuthHandler.GenerateToken(ValidUsername, SessionSecret);

        controller.Login(ValidUsername, ValidPassword, returnUrl: null!);

        var cookies = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
        cookies.Should().Contain(Uri.EscapeDataString(expectedToken));
    }

    [Fact]
    public void LoginPost_ValidCredentials_WithLocalReturnUrl_RedirectsToReturnUrl() {
        var controller = CreateController(EnabledAuthSettings());

        var result = controller.Login(ValidUsername, ValidPassword, returnUrl: "/dashboard");

        result.Should().BeOfType<RedirectResult>();
        var redirect = (RedirectResult)result;
        redirect.Url.Should().Be("/dashboard");
    }

    [Fact]
    public void LoginPost_ValidCredentials_WithNonLocalReturnUrl_RedirectsToHome() {
        var controller = CreateController(EnabledAuthSettings());

        var result = controller.Login(ValidUsername, ValidPassword, returnUrl: "https://evil.com");

        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be("Index");
        redirect.ControllerName.Should().Be("Home");
    }

    [Fact]
    public void LoginPost_InvalidUsername_ReturnsView() {
        var controller = CreateController(EnabledAuthSettings());

        var result = controller.Login("wrong", ValidPassword, returnUrl: null!);

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public void LoginPost_InvalidPassword_ReturnsView() {
        var controller = CreateController(EnabledAuthSettings());

        var result = controller.Login(ValidUsername, "wrong", returnUrl: null!);

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public void LoginPost_InvalidCredentials_ShowsErrorFlashMessage() {
        var controller = CreateController(EnabledAuthSettings());

        controller.Login("wrong", "wrong", returnUrl: null!);

        _flashMessage.Received(1).Error("Invalid username or password.", Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public void LoginPost_InvalidCredentials_PreservesReturnUrlInViewData() {
        var controller = CreateController(EnabledAuthSettings());

        controller.Login("wrong", "wrong", returnUrl: "/stocks");

        controller.ViewData["ReturnUrl"].Should().Be("/stocks");
    }

    [Fact]
    public void LoginPost_InvalidCredentials_DoesNotSetCookie() {
        var controller = CreateController(EnabledAuthSettings());

        controller.Login("wrong", "wrong", returnUrl: null!);

        var cookies = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
        cookies.Should().NotContain(EnvAuthHandler.SchemeName);
    }

    [Fact]
    public void LoginPost_AuthDisabled_RedirectsToHome() {
        var controller = CreateController(DisabledAuthSettings());

        var result = controller.Login("any", "any", returnUrl: null!);

        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be("Index");
        redirect.ControllerName.Should().Be("Home");
    }

    [Fact]
    public void LoginPost_AuthDisabled_DoesNotSetCookie() {
        var controller = CreateController(DisabledAuthSettings());

        controller.Login("any", "any", returnUrl: null!);

        var cookies = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
        cookies.Should().NotContain(EnvAuthHandler.SchemeName);
    }

    [Fact]
    public void LoginPost_AuthDisabled_DoesNotShowFlashMessage() {
        var controller = CreateController(DisabledAuthSettings());

        controller.Login("any", "any", returnUrl: null!);

        _flashMessage.DidNotReceive().Error(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    // ── Logout ─────────────────────────────────────────────────────────

    [Fact]
    public void Logout_RedirectsToHome() {
        var controller = CreateController(EnabledAuthSettings());

        var result = controller.Logout();

        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be("Index");
        redirect.ControllerName.Should().Be("Home");
    }

    [Fact]
    public void Logout_DeletesCookie() {
        var controller = CreateController(EnabledAuthSettings());

        controller.Logout();

        var cookies = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
        cookies.Should().Contain(EnvAuthHandler.SchemeName);
        // Deleted cookies are set with an expiration in the past
        cookies.Should().Contain("expires=");
    }

    // ── Cookie Settings ────────────────────────────────────────────────

    [Fact]
    public void LoginPost_ValidCredentials_CookieIsHttpOnly() {
        var controller = CreateController(EnabledAuthSettings());

        controller.Login(ValidUsername, ValidPassword, returnUrl: null!);

        var cookies = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
        cookies.Should().Contain("httponly", because: "auth cookies must be HttpOnly to prevent XSS");
    }

    [Fact]
    public void LoginPost_ValidCredentials_HttpRequest_CookieIsNotSecure() {
        var controller = CreateController(EnabledAuthSettings(), isHttps: false);

        controller.Login(ValidUsername, ValidPassword, returnUrl: null!);

        var cookies = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
        cookies.Should().NotContain("secure", because: "Secure flag follows Request.IsHttps");
    }

    [Fact]
    public void LoginPost_ValidCredentials_HttpsRequest_CookieIsSecure() {
        var controller = CreateController(EnabledAuthSettings(), isHttps: true);

        controller.Login(ValidUsername, ValidPassword, returnUrl: null!);

        var cookies = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
        cookies.Should().Contain("secure", because: "Secure flag should be set for HTTPS requests");
    }

    [Fact]
    public void LoginPost_ValidCredentials_CookieSameSiteIsStrict() {
        var controller = CreateController(EnabledAuthSettings());

        controller.Login(ValidUsername, ValidPassword, returnUrl: null!);

        var cookies = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
        cookies.Should().Contain("samesite=strict", because: "auth cookies must use SameSite=Strict for CSRF protection");
    }

    [Fact]
    public void LoginPost_ValidCredentials_CookieHasMaxAge() {
        var controller = CreateController(EnabledAuthSettings());

        controller.Login(ValidUsername, ValidPassword, returnUrl: null!);

        var cookies = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
        cookies.Should().Contain("max-age=", because: "cookie should have an expiration via max-age");
    }

    [Fact]
    public void LoginPost_ValidCredentials_CookieMaxAgeIsSevenDays() {
        var controller = CreateController(EnabledAuthSettings());

        controller.Login(ValidUsername, ValidPassword, returnUrl: null!);

        var cookies = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
        var sevenDaysInSeconds = (int)TimeSpan.FromDays(7).TotalSeconds;
        cookies.Should().Contain($"max-age={sevenDaysInSeconds}");
    }
}
