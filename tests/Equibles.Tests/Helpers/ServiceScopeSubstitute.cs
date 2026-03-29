using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Equibles.Tests.Helpers;

public static class ServiceScopeSubstitute {
    public static IServiceScopeFactory Create(params (Type serviceType, object instance)[] registrations) {
        var serviceProvider = Substitute.For<IServiceProvider>();
        foreach (var (serviceType, instance) in registrations) {
            serviceProvider.GetService(serviceType).Returns(instance);
        }

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return scopeFactory;
    }
}
