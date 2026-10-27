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

    [Theory]
    [InlineData(
        "http://www.amada.co.jp/english/index.html",
        "https://ir.amada.co.jp/ir/",
        "https://www.toei-anim.co.jp/"
    )]
    [InlineData(
        "https://www.example.com.cn/",
        "https://ir.example.com.cn/",
        "https://www.another.com.cn/"
    )]
    [InlineData("https://home.kepco.co.kr/", "https://kepco.co.kr/", "https://www.kt.co.kr/")]
    [InlineData(
        "https://www.acme.com.au/investors",
        "https://acme.com.au/",
        "https://www.other.com.au/"
    )]
    public void Cooldown_UnderACountryCodeSecondLevel_ParksOnlyThatCompany(
        string blocked,
        string sameCompany,
        string neighbour
    )
    {
        // Keyed on the shared public suffix, one walled Japanese site parked every .co.jp issuer.
        var gate = Gate();
        gate.RecordRateLimited(blocked);

        gate.IsCoolingDown(sameCompany).Should().BeTrue();
        gate.IsCoolingDown(neighbour).Should().BeFalse();
    }

    [Fact]
    public void Cooldown_OnATwoLetterTopLevelDomain_StillCoversTheSiteAndItsSubdomains()
    {
        var gate = Gate();
        gate.RecordRateLimited("https://www.investis.co/ir");

        gate.IsCoolingDown("https://ir.investis.co/x").Should().BeTrue();
        gate.IsCoolingDown("https://www.other.co/x").Should().BeFalse();
    }

    [Fact]
    public void RecordRateLimited_ZeroOrUnparseableUrl_DoesNotThrow()
    {
        var gate = Gate();

        gate.RecordRateLimited("not a url");
        gate.IsCoolingDown("not a url").Should().BeFalse();
    }
}
