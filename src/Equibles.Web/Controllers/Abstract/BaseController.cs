using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace Equibles.Web.Controllers.Abstract;

[Route("{controller=Home}/{action=Index}")]
public abstract class BaseController : Controller
{
    protected readonly ILogger<BaseController> Logger;

    protected BaseController(ILogger<BaseController> logger)
    {
        Logger = logger;
    }

    protected string GetReturnUrl()
    {
        string returnUrl = null;
        if (Request.HasFormContentType && Request.Form.TryGetValue("ReturnUrl", out var formValue))
        {
            returnUrl = formValue.ToString();
        }
        else if (Request.Query.TryGetValue("ReturnUrl", out var queryValue))
        {
            returnUrl = queryValue.ToString();
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return returnUrl;
        }

        return null;
    }

    protected bool HasReturnUrl()
    {
        return GetReturnUrl() != null;
    }

    protected void InitSseStream()
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
    }

    protected async Task WriteSseEvent(string eventType, object data)
    {
        var json = JsonConvert.SerializeObject(data);
        await Response.WriteAsync($"event: {eventType}\ndata: {json}\n\n");
        await Response.Body.FlushAsync();
    }
}
