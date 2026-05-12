using Equibles.Data.Extensions;
using Equibles.Errors.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Data;

public class RepositoryRegistrationTests {
    [Fact]
    public void AddRepositoriesFrom_AssemblyContainingBaseRepositorySubclass_RegistersItAsScoped() {
        // AddRepositoriesFrom auto-discovers every concrete BaseRepository<T> subclass in the
        // given assembly and binds it to itself with Scoped lifetime. The lifetime is the
        // load-bearing part — Singleton would leak DbContext across requests, Transient would
        // break the unit-of-work guarantee within a single request. Pin it here so a careless
        // change in the registration shape can't silently downgrade the lifetime.
        var services = new ServiceCollection();

        services.AddRepositoriesFrom(typeof(ErrorRepository).Assembly);

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ErrorRepository));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptor.ImplementationType.Should().Be(typeof(ErrorRepository));
    }
}
