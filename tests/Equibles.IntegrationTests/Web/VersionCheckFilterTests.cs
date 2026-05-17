using System.Net;
using System.Text;
using Equibles.Web.Filters;
using Equibles.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

public class VersionCheckFilterTests
{
    private class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StubHttpMessageHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_json, Encoding.UTF8, "application/json"),
                }
            );
    }

    private static VersionCheckService CreateService(string json, bool checkForUpdates)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory
            .CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(new StubHttpMessageHandler(json), disposeHandler: false));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["CheckForUpdates"] = checkForUpdates ? "true" : "false",
                }
            )
            .Build();

        return new VersionCheckService(
            factory,
            configuration,
            Substitute.For<ILogger<VersionCheckService>>()
        );
    }

    private static (ActionExecutingContext context, bool[] nextCalled) CreateActionContext()
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary()
        );
        var executingContext = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object>(),
            new FakeController()
        );
        return (executingContext, new[] { false });
    }

    private static ActionExecutionDelegate Next(bool[] called, ActionExecutingContext ctx) =>
        () =>
        {
            called[0] = true;
            return Task.FromResult(
                new ActionExecutedContext(ctx, new List<IFilterMetadata>(), ctx.Controller)
            );
        };

    [Fact]
    public async Task OnActionExecution_NoUpdate_DoesNotSetViewDataButCallsNext()
    {
        var sut = new VersionCheckFilter(CreateService("{}", checkForUpdates: false));
        var (context, nextCalled) = CreateActionContext();

        await sut.OnActionExecutionAsync(context, Next(nextCalled, context));

        var controller = Assert.IsType<FakeController>(context.Controller);
        Assert.False(controller.ViewData.ContainsKey("VersionUpdate"));
        Assert.True(nextCalled[0]);
    }

    [Fact]
    public async Task OnActionExecution_UpdateAvailable_SetsViewData()
    {
        var service = CreateService("{\"tag_name\":\"v99.0.0\"}", checkForUpdates: true);

        // Prime the cache via the service's background refresh.
        for (var i = 0; i < 100 && service.Get().LatestVersion == null; i++)
        {
            await Task.Delay(20);
        }

        var sut = new VersionCheckFilter(service);
        var (context, nextCalled) = CreateActionContext();

        await sut.OnActionExecutionAsync(context, Next(nextCalled, context));

        var controller = Assert.IsType<FakeController>(context.Controller);
        Assert.True(controller.ViewData.ContainsKey("VersionUpdate"));
        Assert.True(nextCalled[0]);
    }

    private class FakeController : Controller { }
}
