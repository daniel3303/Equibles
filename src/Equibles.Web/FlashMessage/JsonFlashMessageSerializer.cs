using System.Text.Json;
using Equibles.Web.FlashMessage.Contracts;

namespace Equibles.Web.FlashMessage;

public class JsonFlashMessageSerializer : IFlashMessageSerializer {
    public List<IFlashMessageModel> Deserialize(string data) {
        return JsonSerializer.Deserialize<List<FlashMessageModel>>(data)?.Cast<IFlashMessageModel>().ToList();
    }

    public string Serialize(IList<IFlashMessageModel> messages) {
        return JsonSerializer.Serialize(messages);
    }
}
