using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Equibles.Tests.Mcp;

public class EquiblesMcpBuilderTests {
    private readonly IServiceCollection _services;
    private readonly IMcpServerBuilder _mcpServerBuilder;
    private readonly EquiblesMcpBuilder _sut;

    public EquiblesMcpBuilderTests() {
        _services = new ServiceCollection();
        _mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        _sut = new EquiblesMcpBuilder(_services, _mcpServerBuilder);
    }

    [Fact]
    public void AddModule_RegistersModuleAndCallsRegisterTools() {
        _sut.AddModule<FakeModule>();

        _sut.Modules.Should().HaveCount(1);
        _sut.Modules[0].Should().BeOfType<FakeModule>();
        ((FakeModule)_sut.Modules[0]).RegisterToolsCalled.Should().BeTrue();
    }

    [Fact]
    public void AddModule_SameTypeTwice_OnlyAddsOnce() {
        _sut.AddModule<FakeModule>().AddModule<FakeModule>();

        _sut.Modules.Should().HaveCount(1);
    }

    [Fact]
    public void AddModule_DifferentTypes_BothAdded() {
        _sut.AddModule<FakeModule>().AddModule<AnotherFakeModule>();

        _sut.Modules.Should().HaveCount(2);
    }

    [Fact]
    public void AddModule_ReturnsSameBuilder_FluentApi() {
        var result = _sut.AddModule<FakeModule>();

        result.Should().BeSameAs(_sut);
    }

    [Fact]
    public void UseMiddleware_AddsTypeToMiddlewareList() {
        _sut.UseMiddleware<FakeMiddleware>();

        _sut.MiddlewareTypes.Should().Contain(typeof(FakeMiddleware));
    }

    [Fact]
    public void UseMiddleware_RegistersServiceInDi() {
        _sut.UseMiddleware<FakeMiddleware>();

        _services.Should().Contain(sd => sd.ServiceType == typeof(FakeMiddleware) && sd.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void UseMiddleware_ReturnsSameBuilder_FluentApi() {
        var result = _sut.UseMiddleware<FakeMiddleware>();

        result.Should().BeSameAs(_sut);
    }

    // ── Test doubles ────────────────────────────────────────────────────

    private class FakeModule : IEquiblesMcpModule {
        public bool RegisterToolsCalled { get; private set; }
        public void RegisterTools(IMcpServerBuilder builder, IServiceCollection services) {
            RegisterToolsCalled = true;
        }
    }

    private class AnotherFakeModule : IEquiblesMcpModule {
        public void RegisterTools(IMcpServerBuilder builder, IServiceCollection services) { }
    }

    private class FakeMiddleware : IEquiblesMcpMiddleware {
        public Task<object> Invoke(McpToolContext context, Func<Task<object>> next) => next();
    }
}
