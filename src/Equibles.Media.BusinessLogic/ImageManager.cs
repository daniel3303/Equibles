using Equibles.Core.AutoWiring;
using Equibles.Media.Data.Models;
using Equibles.Media.Repositories;
using Microsoft.Extensions.DependencyInjection;
using MimeTypes;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace Equibles.Media.BusinessLogic;

[Service(ServiceLifetime.Scoped, typeof(IImageManager))]
public class ImageManager : IImageManager {
    private readonly ImageRepository _imageRepository;

    public ImageManager(ImageRepository imageRepository) {
        _imageRepository = imageRepository;
    }


    /**
     * <summary>
     *  Saves an image to the database and returns the image object. The db context is not saved.
     *  The file name is used to infer the file extension.
     * </summary>
     * <param name="content">The image content.</param>
     * <param name="fileName">The file name.</param>
     * <param name="maxWidth">The maximum width of the image. 0 to keep the aspect ratio.</param>
     * <param name="maxHeight">The maximum height of the image. 0 to keep the aspect ratio.</param>
     * <returns>The saved image object.</returns>
     */
    public async Task<Image> SaveImage(byte[] content, string fileName, int? maxWidth, int? maxHeight) {
        // Gets the file extension from the file name
        var fileExtension = Path.GetExtension(fileName)?.TrimStart('.');

        // Gets the file name without extension
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

        if (string.IsNullOrEmpty(fileExtension)) {
            throw new ArgumentException("The file extension cannot be null or empty.");
        }

        // Get the content type from the file extension
        var contentType = MimeTypeMap.GetMimeType(fileExtension);
        if (string.IsNullOrEmpty(contentType)) {
            contentType = "application/octet-stream";
        }

        using var contentStream = new MemoryStream(content);
        using var imageProcessor = await SixLabors.ImageSharp.Image.LoadAsync(contentStream);
        if (maxWidth != null || maxHeight != null) {
            imageProcessor.Mutate(i => i.Resize(maxWidth ?? 0, maxHeight ?? 0, new BicubicResampler()));
        }

        var image = new Image() {
            Extension = fileExtension,
            Name = fileNameWithoutExtension,
            Size = content.Length,
            ContentType = contentType,
            Height = imageProcessor.Height,
            Width = imageProcessor.Width,
        };

        image.FileContent = new FileContent() {
            File = image,
            Bytes = content,
        };
        _imageRepository.Add(image);
        return image;
    }

    /// <summary>
    /// Saves an image to the database and returns the image object. The db context is not saved.
    /// </summary>
    /// <param name="image">The image file.</param>
    public void DeleteImage(Image image) {
        if (image == null) return;
        _imageRepository.Delete(image);
    }
}