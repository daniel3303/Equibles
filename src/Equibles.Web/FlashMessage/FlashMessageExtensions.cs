using Equibles.Web.FlashMessage.Contracts;

namespace Equibles.Web.FlashMessage;

public static class FlashMessageExtensions {
    public static void AddFlashMessage(this IServiceCollection services) {
        services.AddTransient<IFlashMessage, FlashMessage>();
        services.AddTransient<IFlashMessageSerializer, JsonFlashMessageSerializer>();
    }
}
