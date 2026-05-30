using Equibles.Data;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Registers only <see cref="RawFilingArtifact"/> for in-memory tests — excludes the
/// Sec entities whose <c>Pgvector.Vector</c> properties are incompatible with the
/// in-memory EF Core provider.
/// </summary>
internal sealed class RawFilingArtifactOnlyModuleConfiguration : IModuleConfiguration
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<RawFilingArtifact>();
    }
}
