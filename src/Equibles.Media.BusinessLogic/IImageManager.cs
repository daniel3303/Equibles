using Equibles.Media.Data.Models;

namespace Equibles.Media.BusinessLogic;

public interface IImageManager {
    public static readonly IList<string> AcceptedExtensions = ["png", "jpg", "jpeg", "gif", "bmp", "webp", "tiff", "svg"];

    public static string AcceptedExtensionsString() {
        return string.Concat(".", string.Join(",.", AcceptedExtensions));
    }

    /// <summary>
    /// Saves an image to the database.
    /// The dbcontext must be saved after this method is called.
    /// </summary>
    /// <param name="content"></param>
    /// <param name="fileName"></param>
    /// <param name="maxWidth"></param>
    /// <param name="maxHeight"></param>
    /// <returns></returns>
    public Task<Image> SaveImage(byte[] content, string fileName, int? maxWidth = 1080, int? maxHeight = 1080);

    /// <summary>
    /// Deletes an image from the database.
    /// The dbcontext must be saved after this method is called.
    /// </summary>
    /// <param name="image"></param>
    public void DeleteImage(Image image);

}
