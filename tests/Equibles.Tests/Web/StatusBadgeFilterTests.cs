using Equibles.Errors.Data;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Equibles.Tests.Helpers;
using Equibles.Web.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Equibles.Tests.Web;

public class StatusBadgeFilterTests {
    private readonly ErrorRepository _errorRepository;
    private readonly ConfigurationBuilder _configBuilder;

    public StatusBadgeFilterTests() {
        var context = TestDbContextFactory.Create(new ErrorsModuleConfiguration());
        _errorRepository = new ErrorRepository(context);
        _configBuilder = new ConfigurationBuilder();
    }

    private StatusBadgeFilter CreateFilter(Dictionary<string, string?>? configValues = null) {
        if (configValues != null) {
            _configBuilder.AddInMemoryCollection(configValues);
        }

        var configuration = _configBuilder.Build();
        return new StatusBadgeFilter(_errorRepository, configuration);
    }

    private static (ActionExecutingContext executingContext, bool[] nextCalled) CreateActionContext() {
        var controller = new FakeController();
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        var executingContext = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller);

        var nextCalled = new[] { false };
        return (executingContext, nextCalled);
    }

    private ActionExecutionDelegate CreateNextDelegate(bool[] nextCalled, ActionExecutingContext executingContext) {
        return () => {
            nextCalled[0] = true;
            return Task.FromResult(new ActionExecutedContext(
                executingContext,
                new List<IFilterMetadata>(),
                executingContext.Controller));
        };
    }

    private async Task SeedErrors(int unseenCount, int seenCount = 0) {
        for (var i = 0; i < unseenCount; i++) {
            _errorRepository.Add(new Error {
                Source = ErrorSource.Other,
                Context = $"unseen-{i}",
                Message = $"Unseen error {i}",
                Seen = false
            });
        }

        for (var i = 0; i < seenCount; i++) {
            _errorRepository.Add(new Error {
                Source = ErrorSource.Other,
                Context = $"seen-{i}",
                Message = $"Seen error {i}",
                Seen = true
            });
        }

        await _errorRepository.SaveChanges();
    }

    private static Dictionary<string, string?> AllConfigPresent() {
        return new Dictionary<string, string?> {
            ["Sec:ContactEmail"] = "test@example.com",
            ["McpApiKey"] = "test-key",
            ["Finra:ClientId"] = "finra-id",
            ["Fred:ApiKey"] = "fred-key",
            ["Embedding:Enabled"] = "true",
            ["Embedding:BaseUrl"] = "http://localhost:11434"
        };
    }

    // ── Badge count: zero ──────────────────────────────────────────────

    [Fact]
    public async Task OnActionExecutionAsync_NoErrorsAndAllConfigPresent_BadgeCountIsZero() {
        var filter = CreateFilter(AllConfigPresent());
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(0);
    }

    // ── Badge count: unseen errors ─────────────────────────────────────

    [Fact]
    public async Task OnActionExecutionAsync_WithUnseenErrors_IncludesThemInBadgeCount() {
        await SeedErrors(unseenCount: 3);
        var filter = CreateFilter(AllConfigPresent());
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(3);
    }

    [Fact]
    public async Task OnActionExecutionAsync_SeenErrorsOnly_NotCountedInBadge() {
        await SeedErrors(unseenCount: 0, seenCount: 5);
        var filter = CreateFilter(AllConfigPresent());
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(0);
    }

    [Fact]
    public async Task OnActionExecutionAsync_MixedSeenAndUnseen_OnlyCountsUnseen() {
        await SeedErrors(unseenCount: 2, seenCount: 4);
        var filter = CreateFilter(AllConfigPresent());
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(2);
    }

    // ── Badge count: config warnings ───────────────────────────────────

    [Fact]
    public async Task OnActionExecutionAsync_AllConfigMissing_CountsFiveWarnings() {
        var filter = CreateFilter();
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(5);
    }

    [Fact]
    public async Task OnActionExecutionAsync_MissingSecContactEmail_AddsOneWarning() {
        var config = AllConfigPresent();
        config["Sec:ContactEmail"] = null;
        var filter = CreateFilter(config);
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(1);
    }

    [Fact]
    public async Task OnActionExecutionAsync_EmptySecContactEmail_AddsOneWarning() {
        var config = AllConfigPresent();
        config["Sec:ContactEmail"] = "";
        var filter = CreateFilter(config);
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(1);
    }

    [Fact]
    public async Task OnActionExecutionAsync_MissingMcpApiKey_AddsOneWarning() {
        var config = AllConfigPresent();
        config["McpApiKey"] = null;
        var filter = CreateFilter(config);
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(1);
    }

    [Fact]
    public async Task OnActionExecutionAsync_MissingFinraClientId_AddsOneWarning() {
        var config = AllConfigPresent();
        config["Finra:ClientId"] = null;
        var filter = CreateFilter(config);
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(1);
    }

    [Fact]
    public async Task OnActionExecutionAsync_MissingFredApiKey_AddsOneWarning() {
        var config = AllConfigPresent();
        config["Fred:ApiKey"] = null;
        var filter = CreateFilter(config);
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(1);
    }

    [Fact]
    public async Task OnActionExecutionAsync_EmbeddingDisabled_AddsOneWarning() {
        var config = AllConfigPresent();
        config["Embedding:Enabled"] = "false";
        var filter = CreateFilter(config);
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(1);
    }

    [Fact]
    public async Task OnActionExecutionAsync_EmbeddingEnabledButBaseUrlMissing_AddsOneWarning() {
        var config = AllConfigPresent();
        config["Embedding:BaseUrl"] = null;
        var filter = CreateFilter(config);
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(1);
    }

    [Fact]
    public async Task OnActionExecutionAsync_EmbeddingEnabledButBaseUrlEmpty_AddsOneWarning() {
        var config = AllConfigPresent();
        config["Embedding:BaseUrl"] = "";
        var filter = CreateFilter(config);
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(1);
    }

    [Fact]
    public async Task OnActionExecutionAsync_EmbeddingKeyMissing_TreatsAsDisabled_AddsOneWarning() {
        var config = AllConfigPresent();
        config.Remove("Embedding:Enabled");
        var filter = CreateFilter(config);
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(1);
    }

    // ── Badge count: combined errors + warnings ────────────────────────

    [Fact]
    public async Task OnActionExecutionAsync_UnseenErrorsPlusConfigWarnings_SumsCorrectly() {
        await SeedErrors(unseenCount: 2);
        var config = AllConfigPresent();
        config["McpApiKey"] = null;
        config["Fred:ApiKey"] = null;
        var filter = CreateFilter(config);
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().Be(4); // 2 errors + 2 warnings
    }

    // ── next() delegation ──────────────────────────────────────────────

    [Fact]
    public async Task OnActionExecutionAsync_AlwaysCallsNext() {
        var filter = CreateFilter(AllConfigPresent());
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        nextCalled[0].Should().BeTrue();
    }

    [Fact]
    public async Task OnActionExecutionAsync_CallsNextEvenWithErrors() {
        await SeedErrors(unseenCount: 5);
        var filter = CreateFilter();
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        nextCalled[0].Should().BeTrue();
    }

    // ── ViewData is set ────────────────────────────────────────────────

    [Fact]
    public async Task OnActionExecutionAsync_SetsViewDataKey() {
        var filter = CreateFilter(AllConfigPresent());
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData.Should().ContainKey("StatusBadgeCount");
    }

    [Fact]
    public async Task OnActionExecutionAsync_ViewDataValueIsInteger() {
        var filter = CreateFilter(AllConfigPresent());
        var (executingContext, nextCalled) = CreateActionContext();

        await filter.OnActionExecutionAsync(executingContext, CreateNextDelegate(nextCalled, executingContext));

        var controller = (Controller)executingContext.Controller;
        controller.ViewData["StatusBadgeCount"].Should().BeOfType<int>();
    }

    // ── Non-Controller context ─────────────────────────────────────────

    [Fact]
    public async Task OnActionExecutionAsync_NonControllerContext_StillCallsNext() {
        var filter = CreateFilter(AllConfigPresent());
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        var nonControllerObject = new object();
        var executingContext = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            nonControllerObject);
        var nextCalled = false;
        ActionExecutionDelegate next = () => {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(
                executingContext,
                new List<IFilterMetadata>(),
                nonControllerObject));
        };

        await filter.OnActionExecutionAsync(executingContext, next);

        nextCalled.Should().BeTrue();
    }

    // ── Helper types ───────────────────────────────────────────────────

    private class FakeController : Controller { }
}
