using BenchmarkDotNet.Attributes;
using Equibles.Web.Authentication;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Per-authenticated-request cost of <see cref="EnvAuthHandler.GenerateToken"/> +
/// <see cref="EnvAuthHandler.ConstantTimeEquals"/>. The auth handler recomputes the expected
/// token on every <c>HandleAuthenticateAsync</c> call — no memoization — and then runs a
/// fixed-time comparison against the cookie value. Each step does its own SHA-256 over a
/// fresh UTF-8 byte array, so the per-request crypto cost is the dominant non-EF work on a
/// hot authenticated route. Captures both paths together as one realistic auth-check loop.
/// </summary>
[MemoryDiagnoser]
public class EnvAuthHandlerBenchmarks {
    private const string Username = "operator";
    private const string SessionSecret = "9f3a7c4d2e1b6f8a0c5d4e9b3a7f2c1d";
    private string _cookie;

    [GlobalSetup]
    public void Setup() {
        // Pre-compute the cookie a real authenticated session would carry. Without this the
        // ConstantTimeEquals path would always compare against a fresh token, masking the
        // realistic match case.
        _cookie = EnvAuthHandler.GenerateToken(Username, SessionSecret);
    }

    [Benchmark]
    public bool ValidateAuthenticatedCookie() {
        // Mirrors the production sequence: regenerate the expected token from settings, then
        // FixedTimeEquals against the request's cookie. One pass = one authenticated request.
        var expected = EnvAuthHandler.GenerateToken(Username, SessionSecret);
        return EnvAuthHandler.ConstantTimeEquals(_cookie, expected);
    }
}
