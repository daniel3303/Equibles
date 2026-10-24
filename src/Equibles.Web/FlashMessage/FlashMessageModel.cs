using Equibles.Web.FlashMessage.Contracts;

namespace Equibles.Web.FlashMessage;

public class FlashMessageModel : IFlashMessageModel
{
    public string Title { get; set; }
    public string Message { get; set; }
    public FlashMessageType Type { get; set; }

    public FlashMessageModel()
    {
        Type = FlashMessageType.Success;
    }
}
