using Equibles.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Equibles.UnitTests.Worker;

public class OutboundHostGateTests
{
    private static OutboundHostGate Gate(int minIntervalMs = 0, int cooldownMinutes = 180) =>
        new(
            Options.Create(
                new OutboundHostGateOptions
                {
                    MinIntervalMilliseconds = minIntervalMs,
                    CooldownMinutes = cooldownMinutes,
                }
            ),
            NullLogger<OutboundHostGate>.Instance
        );

    [Fact]
    public async Task NoCooldown_WaitForTurn_Completes()
    {
        var gate = Gate();

        gate.IsCoolingDown("https://acme.com/investors").Should().BeFalse();
        await gate.WaitForTurn("https://acme.com/investors", CancellationToken.None);
    }

    [Fact]
    public async Task AfterRateLimited_WaitForTurn_ThrowsCoolingDown()
    {
        var gate = Gate();
        gate.RecordRateLimited("https://acme.com/investors");

        var act = () => gate.WaitForTurn("https://acme.com/ir", CancellationToken.None);

        await act.Should().ThrowAsync<HostCoolingDownException>();
    }

    [Fact]
    public void Cooldown_IsKeyedByRegistrableDomain_SoSubdomainsShareIt()
    {
        // A 1015 ban is per Cloudflare zone (apex), so cooling down a subdomain must cool down the
        // apex and its other subdomains too.
        var gate = Gate();
        gate.RecordRateLimited("https://investors.bjs.com/events");

        gate.IsCoolingDown("https://bjs.com/anything").Should().BeTrue();
        gate.IsCoolingDown("https://ir.bjs.com/x").Should().BeTrue();
        gate.IsCoolingDown("https://other.com/x").Should().BeFalse();
    }

    [Fact]
    public void RecordRateLimited_ZeroOrUnparseableUrl_DoesNotThrow()
    {
        var gate = Gate();

        gate.RecordRateLimited("not a url");
        gate.IsCoolingDown("not a url").Should().BeFalse();
    }
}
