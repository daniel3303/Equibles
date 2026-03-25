using Equibles.Web.Authentication;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.FlashMessage.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Equibles.Web.Controllers;

[AllowAnonymous]
public class AuthController : BaseController {
    private readonly AuthSettings _authSettings;
    private readonly IFlashMessage _flashMessage;

    public AuthController(
        ILogger<BaseController> logger,
        IOptions<AuthSettings> authSettings,
        IFlashMessage flashMessage) : base(logger) {
        _authSettings = authSettings.Value;
        _flashMessage = flashMessage;
    }

    [HttpGet]
    public IActionResult Login(string returnUrl) {
        if (!_authSettings.IsEnabled) {
            return RedirectToAction("Index", "Home");
        }

        if (User.Identity?.IsAuthenticated == true
            && User.Identity.Name != EnvAuthHandler.AnonymousUsername) {
            return RedirectToAction("Index", "Home");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Login(string username, string password, string returnUrl) {
        if (!_authSettings.IsEnabled) {
            return RedirectToAction("Index", "Home");
        }

        var usernameMatch = EnvAuthHandler.ConstantTimeEquals(username, _authSettings.Username);
        var passwordMatch = EnvAuthHandler.ConstantTimeEquals(password, _authSettings.Password);

        if (!usernameMatch || !passwordMatch) {
            _flashMessage.Error("Invalid username or password.");
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        var token = EnvAuthHandler.GenerateToken(_authSettings.Username, _authSettings.SessionSecret);
        Response.Cookies.Append(EnvAuthHandler.SchemeName, token, new CookieOptions {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(7)
        });

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout() {
        Response.Cookies.Delete(EnvAuthHandler.SchemeName);
        return RedirectToAction("Index", "Home");
    }
}
