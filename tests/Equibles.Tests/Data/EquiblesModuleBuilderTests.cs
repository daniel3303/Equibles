using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.Data;

public class EquiblesModuleBuilderTests {
    private class FakeModuleA : IModuleConfiguration {
        public void ConfigureEntities(ModelBuilder builder) { }
    }

    private class FakeModuleB : IModuleConfiguration {
        public void ConfigureEntities(ModelBuilder builder) { }
    }

    [Fact]
    public void NewBuilder_HasEmptyModulesList() {
        var builder = new EquiblesModuleBuilder();

        builder.Modules.Should().BeEmpty();
    }

    [Fact]
    public void AddModule_AddsModuleToList() {
        var builder = new EquiblesModuleBuilder();

        builder.AddModule<FakeModuleA>();

        builder.Modules.Should().ContainSingle()
            .Which.Should().BeOfType<FakeModuleA>();
    }

    [Fact]
    public void AddModule_SameTypeTwice_OnlyAddsOnce() {
        var builder = new EquiblesModuleBuilder();

        builder.AddModule<FakeModuleA>();
        builder.AddModule<FakeModuleA>();

        builder.Modules.Should().ContainSingle();
    }

    [Fact]
    public void AddModule_DifferentTypes_AddsBoth() {
        var builder = new EquiblesModuleBuilder();

        builder.AddModule<FakeModuleA>();
        builder.AddModule<FakeModuleB>();

        builder.Modules.Should().HaveCount(2);
        builder.Modules.Should().ContainSingle(m => m is FakeModuleA);
        builder.Modules.Should().ContainSingle(m => m is FakeModuleB);
    }

    [Fact]
    public void AddModule_ReturnsBuilder_ForFluentApi() {
        var builder = new EquiblesModuleBuilder();

        var result = builder.AddModule<FakeModuleA>();

        result.Should().BeSameAs(builder);
    }
}
