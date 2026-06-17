using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Equibles.Data;

/// <summary>
/// Model-cache key that folds the context's module set into the key. EF Core
/// caches the built model per context type by default; that is correct in
/// production, where each context type resolves exactly one module set, but tests
/// build the same context type with different module subsets. With a type-only
/// key whichever model builds first serves every later context of that type,
/// dropping entities the later context expected (its <c>FindEntityType</c> then
/// returns null). Folding the module set into the key gives each distinct
/// composition its own cached model. In production the set is constant per
/// context type, so this collapses back to a single cache entry — same behaviour
/// as the default factory.
/// </summary>
public sealed class ModuleAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is EquiblesDbContextBase equiblesContext
            ? (context.GetType(), equiblesContext.ModuleCacheKey, designTime)
            : (context.GetType(), designTime);
}
