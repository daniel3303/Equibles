namespace Equibles.Web.FlashMessage.Contracts;

public interface IFlashMessage
{
    public List<IFlashMessageModel> Peek();
    public List<IFlashMessageModel> Retrieve();
    public void Clear();
    public void Success(string message, string title = null);
    public void Error(string message, string title = null);
    public void Info(string message, string title = null);
    public void Warning(string message, string title = null);
}
