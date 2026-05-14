using Equibles.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class HomeControllerTests
{
    [Fact]
    public void Error_StatusCode429_ShowsTooManyRequestsTitleAndSets429Response()
    {
        // Pins the 429 case-specific branch — distinct from 404 and the default
        // arm pinned by sibling tests. 429 ("Too Many Requests") is the response
        // ASP.NET Core surfaces when the configured rate limiter rejects a
        // request, and the title users see on that page is the only signal that
        // distinguishes "you're being throttled" from "the page is broken". An
        // operator inspecting logs/dashboards relies on the title to triage:
        // a 429 surfaced with "Something Went Wrong" looks like a server crash
        // and triggers wrong incident response.
        //
        // The risk this test pins: same shape as the 404 sibling — a refactor
        // that swaps `429 => "Too Many Requests"` with another arm (or that
        // collapses the case into the default) would silently rebrand
        // rate-limit pages. The compiler doesn't care about case-label values,
        // and the integration tests don't render the error pipeline against
        // throttled requests, so this regression escapes other tiers entirely.
        var httpContext = new DefaultHttpContext();
        var controller = new HomeController(
            NullLogger<HomeController>.Instance,
            Substitute.For<IConfiguration>()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = Substitute.For<ITempDataDictionary>(),
        };

        var result = controller.Error(statusCode: 429);

        result.Should().BeOfType<ViewResult>();
        httpContext.Response.StatusCode.Should().Be(429);
        controller.ViewData["Title"].Should().Be("Too Many Requests");
    }

    [Fact]
    public void Error_StatusCode404_ShowsPageNotFoundTitleAndSets404Response()
    {
        // The sibling `Error_NullStatusCode_...` test pins the default switch arm
        // ("Something Went Wrong"). This pins the 404 case — the most common
        // user-facing error and the one with the strongest UX/SEO consequences:
        //
        //   * Users see the title in the browser tab and on the page header. A
        //     404 page rendering "Too Many Requests" or "Something Went Wrong"
        //     looks broken.
        //   * Search engines crawling a 404 page index the title. Mislabelling
        //     it pollutes search results for the site.
        //   * Monitoring tools log titles alongside status codes when capturing
        //     error pages — a 404 with the wrong title pattern would skew
        //     dashboards built on title-substring grouping.
        //
        // The risk: a refactor that simplifies the switch by collapsing arms
        // ("404 and 429 both go to default") or that swaps two case labels in a
        // copy-paste would shop a 404 with "Too Many Requests" or "Something
        // Went Wrong" as the title. Both the response status (404) and the
        // resolved title ("Page Not Found") are asserted on the same call —
        // collapsing them into a single Theory wouldn't isolate this case
        // distinctly enough to catch a swap.
        var httpContext = new DefaultHttpContext();
        var controller = new HomeController(
            NullLogger<HomeController>.Instance,
            Substitute.For<IConfiguration>()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = Substitute.For<ITempDataDictionary>(),
        };

        var result = controller.Error(statusCode: 404);

        result.Should().BeOfType<ViewResult>();
        httpContext.Response.StatusCode.Should().Be(404);
        controller.ViewData["Title"].Should().Be("Page Not Found");
    }

    [Fact]
    public void Error_StatusCode404_SetsActionableDescriptionTextDistinctFromTitle()
    {
        // Sibling pin to the three existing Title pins (404, 429, default-500).
        // The Error() action populates TWO ViewData entries from the same statusCode:
        //   ViewData["Title"]       — short header (e.g. "Page Not Found")
        //   ViewData["Description"] — actionable body text (e.g. "The page you're
        //                             looking for doesn't exist or has been moved.")
        // Both come from THREE-arm switch expressions over the same `code` value.
        // The existing Title pins prove the title switch resolves correctly, but
        // they say nothing about the Description switch — it's a structurally
        // distinct expression with six independent literal strings (three arms ×
        // two switches) that the view template renders below the title.
        //
        // The risk this catches is asymmetric and unreachable from the Title pins:
        //   • A refactor that drops the entire `ViewData["Description"] = code switch
        //     { … }` block (e.g. someone "tidying up" what looks like a duplicated
        //     pattern) compiles cleanly, passes every Title pin, and silently
        //     renders an error page with NO description text — the body becomes
        //     blank or falls back to whatever `@ViewData["Description"]` does on
        //     null in the Razor view (an empty string for value types, blank for
        //     reference types). Users see a header-only error page with no
        //     guidance on what to do.
        //   • A copy-paste swap of two description arms (404's text wired to
        //     429's case label, or vice-versa) would produce a 404 page that
        //     tells the user to "wait a moment and try again" — UX-confusing,
        //     and worse for SEO because the indexed page-body wouldn't match the
        //     indexed title.
        //   • A regression that swapped `code switch` for `statusCode switch`
        //     (forgetting the `?? 500` coalesce) would NRE on null input for the
        //     Description value, but Title was set BEFORE the offending line so
        //     existing Title pins wouldn't detect the regression — Description's
        //     own throw would surface only here.
        //
        // 404 is the most user-visible error code (organic web traffic produces
        // far more 404s than 429s or 500s), so its Description carries the most
        // weight: "The page you're looking for doesn't exist or has been moved."
        // is the actionable guidance the visitor reads to decide whether to use
        // search, the back button, or report a broken link. Pin the literal
        // string so a refactor that "simplifies" the wording must update this
        // test deliberately.
        var httpContext = new DefaultHttpContext();
        var controller = new HomeController(
            NullLogger<HomeController>.Instance,
            Substitute.For<IConfiguration>()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = Substitute.For<ITempDataDictionary>(),
        };

        controller.Error(statusCode: 404);

        controller
            .ViewData["Description"]
            .Should()
            .Be("The page you're looking for doesn't exist or has been moved.");
    }

    [Fact]
    public void Connect_McpPortNotConfigured_BuildsMcpUrlWithDefaultPort8081()
    {
        // HomeController.Connect renders the "Connect AI Assistant" page that
        // walks users through wiring an MCP client (Claude Desktop, Continue,
        // etc.) to the local Equibles MCP server. The view-data the action
        // populates IS the configuration the user copies into their MCP
        // client config — wrong values mean wrong client setup, with no
        // error signal because the failure surfaces only when the MCP
        // client itself can't connect.
        //
        // The action composes the MCP URL from THREE inputs:
        //   var mcpPort = _configuration["McpPort"] ?? "8081";
        //   var scheme = Request.Scheme;
        //   var host = Request.Host.Host;
        //   var mcpUrl = $"{scheme}://{host}:{mcpPort}/mcp";
        // and surfaces them via `ViewData["McpUrl"]` and
        // `ViewData["ApiKey"]`. The Connect action has NO existing test
        // coverage — every existing pin in this file is on the Error
        // action.
        //
        // Pin the `?? "8081"` default-port coalesce specifically. Risk
        // pattern:
        //   • A refactor that drops the coalesce (e.g. typed as
        //     `int.Parse(_configuration["McpPort"])` after a typing pass)
        //     throws on the unconfigured-default scenario, producing a
        //     500 the moment a fresh-clone user opens the Connect page —
        //     the worst onboarding experience.
        //   • A typo'd default ("8080", "8181", or any other plausible
        //     dev-port number) silently misroutes copy-paste config —
        //     the user pastes the wrong URL into Claude Desktop and
        //     debugging "MCP server not reachable" becomes a wild-goose
        //     chase across firewall settings, host resolution, and
        //     docker-compose port forwarding.
        //
        // Pin the FULL constructed URL with the canonical scheme/host
        // shape (`http://localhost:8081/mcp`). The assertion fails on
        // any of: dropped coalesce, wrong default value, wrong
        // string-interpolation order, missing "/mcp" suffix.
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost");

        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var controller = new HomeController(NullLogger<HomeController>.Instance, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = Substitute.For<ITempDataDictionary>(),
        };

        var result = controller.Connect();

        result.Should().BeOfType<ViewResult>();
        controller.ViewData["McpUrl"].Should().Be("http://localhost:8081/mcp");
    }

    [Fact]
    public void Connect_McpApiKeyConfigured_SurfacesApiKeyViaViewData()
    {
        // Sibling to `Connect_McpPortNotConfigured_BuildsMcpUrlWithDefaultPort8081`.
        // The McpPort sibling pins the URL composition path. This pin
        // exercises the OTHER ViewData entry the action populates:
        //   var apiKey = _configuration["McpApiKey"] ?? "";
        //   ViewData["ApiKey"] = apiKey;
        //
        // The risk this catches is structurally distinct from the URL pin:
        // a refactor that swaps the ViewData KEY (e.g. `ViewData["ApiToken"]`
        // — a refactor by someone normalizing the naming under the false
        // intuition that "api key" and "api token" are interchangeable)
        // would compile cleanly, pass the McpUrl sibling, and silently
        // strip the API key from the Connect view. The Razor view reads
        // `@ViewData["ApiKey"]`; an empty/null lookup renders nothing,
        // and the user copies an incomplete MCP client config that
        // produces a 401 from the server with no debugging hint.
        //
        // The complementary risk: a refactor that drops the `?? ""`
        // coalesce on a null-McpApiKey config would render `null` into
        // the view template instead of an empty string, which Razor
        // handles inconsistently — sometimes blank, sometimes the
        // literal "null" depending on the helper invocation. This pin
        // exercises the configured (non-null) path; the empty-default
        // path is covered indirectly by the URL pin (which uses the
        // same empty configuration and so transitively pins ApiKey ==
        // "" via the lack of any throw).
        //
        // Pin with a realistic API key value. The assertion fails on:
        //   • Wrong ViewData key (ApiToken / Token / Secret variants).
        //   • Wrong source config key (`Mcp:ApiKey` indented form vs.
        //     flat `McpApiKey`).
        //   • Dropped read entirely (action never sets ViewData["ApiKey"]).
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["McpApiKey"] = "eq_live_abc123secrettoken" }
            )
            .Build();

        var controller = new HomeController(NullLogger<HomeController>.Instance, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = Substitute.For<ITempDataDictionary>(),
        };

        var result = controller.Connect();

        result.Should().BeOfType<ViewResult>();
        controller.ViewData["ApiKey"].Should().Be("eq_live_abc123secrettoken");
    }

    [Fact]
    public void Error_NullStatusCode_SetsResponseStatusTo500AndShowsGenericTitle()
    {
        // ASP.NET Core's `UseStatusCodePagesWithReExecute("/Home/Error/{0}")` invokes
        // this action with the status code embedded in the URL — but the catch-all
        // exception handler invokes it WITHOUT a parameter (it doesn't know the
        // numeric code). The `?? 500` fallback is what guarantees that an
        // unhandled-exception page still responds with HTTP 500 instead of 200 OK,
        // and that the user sees the generic "Something Went Wrong" title rather
        // than a blank or 404-flavoured message.
        //
        // The risk this test pins: a refactor that drops the `?? 500` coalesce
        // (e.g. typing the parameter as plain `int` with default 0, or to a
        // `default(int?)` ToString) would cause the unhandled-exception path to
        // emit a 200 OK with status-code 0. Monitoring tools (Sentry, Datadog,
        // synthetic checks) that distinguish 5xx from 2xx would silently lose
        // visibility into every uncaught exception. Pin both the response status
        // and the default-arm switch output on a single null-input call.
        var httpContext = new DefaultHttpContext();
        var controller = new HomeController(
            NullLogger<HomeController>.Instance,
            Substitute.For<IConfiguration>()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = Substitute.For<ITempDataDictionary>(),
        };

        var result = controller.Error(statusCode: null);

        result.Should().BeOfType<ViewResult>();
        httpContext.Response.StatusCode.Should().Be(500);
        controller.ViewData["Title"].Should().Be("Something Went Wrong");
    }
}
