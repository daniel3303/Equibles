using Equibles.Web.Controllers;
using Equibles.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Web;

// The collection serialises this test against ChangelogServiceRenderHtmlTests:
// both manipulate AppContext.BaseDirectory/CHANGELOG.md and would race under
// xUnit's default per-class parallelism. The CollectionDefinition lives in
// ChangelogFileCollection.cs in this folder.
[Collection(ChangelogFileCollection.Name)]
public class ChangelogControllerIndexFallbackTests
{
    // Index's documented contract is a graceful fallback when CHANGELOG.md was
    // not shipped with the build — redirect to the canonical GitHub copy so
    // users never hit a broken page. A typo in the URL, a future refactor that
    // returns 404 instead of redirecting, or a repo rename without test update
    // would silently degrade that fallback. The redirect URL is hard-coded in
    // the controller, so the contract is "this exact URL", not a pattern.
    [Fact]
    public void Index_ChangelogFileMissing_RedirectsToCanonicalGitHubChangelogUrl()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
        var hadExisting = File.Exists(path);
        var backup = hadExisting ? File.ReadAllText(path) : null;

        try
        {
            if (hadExisting)
            {
                File.Delete(path);
            }

            var controller = new ChangelogController(
                NullLogger<ChangelogController>.Instance,
                new ChangelogService()
            );

            var result = controller.Index();

            var redirect = result.Should().BeOfType<RedirectResult>().Subject;
            redirect
                .Url.Should()
                .Be("https://github.com/daniel3303/Equibles/blob/main/CHANGELOG.md");
        }
        finally
        {
            if (backup != null)
            {
                File.WriteAllText(path, backup);
            }
        }
    }
}
