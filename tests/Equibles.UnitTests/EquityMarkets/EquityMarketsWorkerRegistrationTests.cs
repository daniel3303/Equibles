using Equibles.EquityMarkets.HostedService.Extensions;
using Equibles.Integrations.Bme;
using Equibles.Integrations.Esma;
using Equibles.Integrations.Euronext;
using Equibles.Integrations.Gleif;
using Equibles.Integrations.Gpw;
using Equibles.Integrations.Lse;
using Equibles.Integrations.NasdaqNordic;
using Equibles.Integrations.Xetra;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.EquityMarkets;

public class EquityMarketsWorkerRegistrationTests
{
    // Typed-client activation needs exactly one applicable constructor; a second one leaves the client
    // unresolvable and every directory tick faulting, so each source client is resolved here.
    [Theory]
    [InlineData(typeof(GleifIdentityClient))]
    [InlineData(typeof(EuronextDirectoryClient))]
    [InlineData(typeof(XetraInstrumentListClient))]
    [InlineData(typeof(NasdaqNordicClient))]
    [InlineData(typeof(BmeClient))]
    [InlineData(typeof(GpwClient))]
    [InlineData(typeof(LseInstrumentListClient))]
    [InlineData(typeof(EsmaFirdsClient))]
    [InlineData(typeof(FcaFirdsClient))]
    public void EverySourceClient_ResolvesAsATypedHttpClient(Type client)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEquityMarketsWorker();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService(client).Should().BeOfType(client);
    }
}
