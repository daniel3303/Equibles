using System.Reflection;
using Equibles.Integrations.Yahoo;
using Xunit;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// <c>InvalidateSession</c> is reached in production only via the 401/403
/// session-refresh path of <c>SendWithRetry</c> (which re-acquires over a
/// non-injectable HttpClient and so isn't exercised by the other Yahoo pins).
/// This invokes it directly: a populated session must be fully cleared so the
/// next <c>EnsureSession</c> re-acquires. The shared statics are snapshotted
/// and restored so other Yahoo tests are unaffected.
/// </summary>
public class YahooFinanceClientInvalidateSessionTests
{
    private static readonly FieldInfo CrumbField = typeof(YahooFinanceClient).GetField(
        "_cachedCrumb",
        BindingFlags.NonPublic | BindingFlags.Static
    );
    private static readonly FieldInfo CookieField = typeof(YahooFinanceClient).GetField(
        "_cachedCookieHeader",
        BindingFlags.NonPublic | BindingFlags.Static
    );
    private static readonly FieldInfo ExpiryField = typeof(YahooFinanceClient).GetField(
        "_sessionExpiry",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public async Task InvalidateSession_PopulatedSession_ClearsCrumbCookieAndExpiry()
    {
        var prevCrumb = CrumbField.GetValue(null);
        var prevCookie = CookieField.GetValue(null);
        var prevExpiry = ExpiryField.GetValue(null);
        try
        {
            CrumbField.SetValue(null, "live-crumb");
            CookieField.SetValue(null, "A=1; B=2");
            ExpiryField.SetValue(null, DateTime.UtcNow.AddMinutes(30));

            var method = typeof(YahooFinanceClient).GetMethod(
                "InvalidateSession",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            await (Task)method.Invoke(null, null);

            CrumbField.GetValue(null).Should().BeNull();
            CookieField.GetValue(null).Should().BeNull();
            ((DateTime)ExpiryField.GetValue(null))
                .Should()
                .Be(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
        }
        finally
        {
            CrumbField.SetValue(null, prevCrumb);
            CookieField.SetValue(null, prevCookie);
            ExpiryField.SetValue(null, prevExpiry);
        }
    }
}
