namespace Equibles.Web.FlashMessage.Contracts;

public interface IFlashMessageModel
{
    public string Title { get; set; }
    public string Message { get; set; }
    public FlashMessageType Type { get; set; }
}
