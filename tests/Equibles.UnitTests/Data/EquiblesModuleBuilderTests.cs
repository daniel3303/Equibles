using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Data;

public class EquiblesModuleBuilderTests
{
    private class FakeModuleA : IModuleConfiguration
    {
        public void ConfigureEntities(ModelBuilder builder) { }
    }

    private class FakeModuleB : IModuleConfiguration
    {
        public void ConfigureEntities(ModelBuilder builder) { }
    }

    [Fact]
    public void NewBuilder_HasEmptyModulesList()
    {
        var builder = new EquiblesModuleBuilder();

        builder.Modules.Should().BeEmpty();
    }

    [Fact]
    public void AddModule_AddsModuleToList()
    {
        var builder = new EquiblesModuleBuilder();

        builder.AddModule<FakeModuleA>();

        builder.Modules.Should().ContainSingle().Which.Should().BeOfType<FakeModuleA>();
    }

    [Fact]
    public void AddModule_SameTypeTwice_OnlyAddsOnce()
    {
        var builder = new EquiblesModuleBuilder();

        builder.AddModule<FakeModuleA>();
        builder.AddModule<FakeModuleA>();

        builder.Modules.Should().ContainSingle();
    }

    [Fact]
    public void AddModule_DifferentTypes_AddsBoth()
    {
        var builder = new EquiblesModuleBuilder();

        builder.AddModule<FakeModuleA>();
        builder.AddModule<FakeModuleB>();

        builder.Modules.Should().HaveCount(2);
        builder.Modules.Should().ContainSingle(m => m is FakeModuleA);
        builder.Modules.Should().ContainSingle(m => m is FakeModuleB);
    }

    [Fact]
    public void AddModule_ReturnsBuilder_ForFluentApi()
    {
        var builder = new EquiblesModuleBuilder();

        var result = builder.AddModule<FakeModuleA>();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddAllModules_DoesNotDuplicateAlreadyAddedModules()
    {
        // AddAllModules scans every loaded assembly for IModuleConfiguration
        // implementations. The composition root may explicitly call
        // AddModule<T>() for a specific module before invoking AddAllModules
        // (e.g. when a custom module precedes the default scan order). The
        // dedup guard at the top of AddAllModules — `if (Modules.Any(m =>
        // m.GetType() == type)) continue;` — protects against double-
        // registering, which would attach the entity configurations twice
        // and crash ModelBuilder with "duplicate key" errors at startup.
        // Pin the dedup so a refactor that drops the guard surfaces here
        // rather than as an opaque EF Core startup exception.
        var builder = new EquiblesModuleBuilder();
        builder.AddModule<FakeModuleA>();

        builder.AddAllModules();

        builder.Modules.Should().ContainSingle(m => m is FakeModuleA);
    }
}
