using Equibles.Web.FlashMessage.Contracts;

namespace Equibles.Web.FlashMessage;

public class FlashMessageModel : IFlashMessageModel {
    public bool IsHtml { get; set; }
    public string Title { get; set; }
    public string Message { get; set; }
    public FlashMessageType Type { get; set; }

    public FlashMessageModel() {
        IsHtml = false;
        Type = FlashMessageType.Success;
    }
}
