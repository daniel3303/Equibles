using Equibles.Congress.HostedService.Services;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

[Collection(ParadeDbCollection.Name)]
public class SenateAccessGateTests : ParadeDbMcpTestBase
{
    public SenateAccessGateTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task ThrowIfHeld_AfterADenial_HoldsBothLanesForADayThenRetries()
    {
        var reporter = new ErrorReporter(
            ServiceScopeSubstitute.Create((typeof(ErrorManager), ErrorManager)),
            NullLogger<ErrorReporter>()
        );
        var gate = new SenateAccessGate(
            new ErrorRepository(DbContext),
            NullLogger<SenateAccessGate>()
        );

        await SenateAccessGate.Record(
            new SenateAccessDeniedException("Senate eFD denied access", observed: true),
            reporter,
            NullLogger<SenateAccessGate>(),
            "Senate PTR fetch"
        );

        var held = async () => await gate.ThrowIfHeld(CancellationToken.None);
        var skipped = (await held.Should().ThrowAsync<SenateAccessDeniedException>()).Which;
        skipped.Observed.Should().BeFalse();
        await SenateAccessGate.Record(
            skipped,
            reporter,
            NullLogger<SenateAccessGate>(),
            "Senate annual report fetch"
        );
        (await DbContext.Set<Error>().CountAsync()).Should().Be(1);

        await DbContext
            .Set<Error>()
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(
                    error => error.CreationTime,
                    DateTime.UtcNow - SenateAccessGate.HoldInterval - TimeSpan.FromMinutes(1)
                )
            );

        await held.Should().NotThrowAsync();
    }
}
