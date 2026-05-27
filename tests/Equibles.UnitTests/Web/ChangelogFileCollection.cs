namespace Equibles.UnitTests.Web;

// Serialises any test that mutates AppContext.BaseDirectory/CHANGELOG.md so
// the two coexisting backup+restore patterns (write-and-restore in the
// service test, delete-and-restore in the controller fallback test) don't
// race under xUnit's default per-class parallelism.
[CollectionDefinition(Name)]
public class ChangelogFileCollection
{
    public const string Name = "ChangelogFile";
}
