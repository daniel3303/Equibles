using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Equibles.Web.Authentication;

public class EnvAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions> {
    public const string SchemeName = "EnvAuth";
    public const string AnonymousUsername = "anonymous";

    private readonly AuthSettings _authSettings;

    public EnvAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<AuthSettings> authSettings)
        : base(options, logger, encoder) {
        _authSettings = authSettings.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        if (!_authSettings.IsEnabled) {
            var anonymousClaims = new[] { new Claim(ClaimTypes.Name, AnonymousUsername) };
            var anonymousIdentity = new ClaimsIdentity(anonymousClaims, SchemeName);
            var anonymousPrincipal = new ClaimsPrincipal(anonymousIdentity);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(anonymousPrincipal, SchemeName)));
        }

        var cookie = Request.Cookies[SchemeName];
        if (string.IsNullOrEmpty(cookie)) {
            return Task.FromResult(AuthenticateResult.Fail("Not authenticated"));
        }

        var expectedToken = GenerateToken(_authSettings.Username, _authSettings.SessionSecret);
        if (!ConstantTimeEquals(cookie, expectedToken)) {
            return Task.FromResult(AuthenticateResult.Fail("Invalid session"));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, _authSettings.Username) };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) {
        var returnUrl = Request.Path + Request.QueryString;
        Response.Redirect($"/Auth/Login?ReturnUrl={Uri.EscapeDataString(returnUrl)}");
        return Task.CompletedTask;
    }

    public static string GenerateToken(string username, string sessionSecret) {
        var input = $"{username}:{sessionSecret}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash);
    }

    public static bool ConstantTimeEquals(string a, string b) {
        var aHash = SHA256.HashData(Encoding.UTF8.GetBytes(a ?? ""));
        var bHash = SHA256.HashData(Encoding.UTF8.GetBytes(b ?? ""));
        return CryptographicOperations.FixedTimeEquals(aHash, bHash);
    }
}
