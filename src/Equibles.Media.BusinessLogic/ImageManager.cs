using Equibles.Core.AutoWiring;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Data.Models;
using Equibles.Media.Repositories;
using Microsoft.Extensions.DependencyInjection;
using MimeTypes;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace Equibles.Media.BusinessLogic;

[Service(ServiceLifetime.Scoped, typeof(IImageManager))]
public class ImageManager : IImageManager
{
    private readonly ImageRepository _imageRepository;
    private readonly FileStorageRouter _storageRouter;

    public ImageManager(ImageRepository imageRepository, FileStorageRouter storageRouter)
    {
        _imageRepository = imageRepository;
        _storageRouter = storageRouter;
    }

    /**
     * <summary>
     *  Saves an image to the database and returns the image object. The db context is not saved.
     *  The file name is used to infer the file extension.
     * </summary>
     * <param name="content">The image content.</param>
     * <param name="fileName">The file name.</param>
     * <param name="maxWidth">The maximum width of the image. Null or 0 leaves it unbounded.</param>
     * <param name="maxHeight">The maximum height of the image. Null or 0 leaves it unbounded.</param>
     * <returns>The saved image object.</returns>
     */
    public async Task<Image> SaveImage(
        byte[] content,
        string fileName,
        int? maxWidth,
        int? maxHeight
    )
    {
        var fileExtension = Path.GetExtension(fileName)?.TrimStart('.');

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

        if (string.IsNullOrEmpty(fileExtension))
        {
            throw new ArgumentException("The file extension cannot be null or empty.");
        }

        var contentType = MimeTypeMap.GetMimeType(fileExtension);
        if (string.IsNullOrEmpty(contentType))
        {
            contentType = "application/octet-stream";
        }

        using var contentStream = new MemoryStream(content);
        using var imageProcessor = await SixLabors.ImageSharp.Image.LoadAsync(contentStream);
        // maxWidth/maxHeight are a *maximum* — only resize when the source
        // actually exceeds them. A source already within bounds must not be
        // enlarged (wastes storage and blurs the image).
        // A null or non-positive bound is unconstrained, matching the doc — checking it here
        // (not just at the Size below) also keeps a "0 = no cap" configuration from ever
        // producing a Size(0, 0), which ImageSharp rejects.
        var exceedsMaxBounds =
            (maxWidth is > 0 && imageProcessor.Width > maxWidth)
            || (maxHeight is > 0 && imageProcessor.Height > maxHeight);
        if (exceedsMaxBounds)
        {
            // The bounds are a bounding BOX: ResizeMode.Max fits the image inside them and
            // preserves the aspect ratio. The previous Resize(width, height) overload resolved
            // to ResizeMode.Crop, which recorded an exact centre-cropped box (a 2408x1648
            // upload became 2400x2400) — invisible on screen only because the ORIGINAL bytes
            // were stored, so the recorded dimensions lied instead.
            imageProcessor.Mutate(i =>
                i.Resize(
                    new ResizeOptions
                    {
                        Size = new SixLabors.ImageSharp.Size(
                            maxWidth is > 0 ? maxWidth.Value : 0,
                            maxHeight is > 0 ? maxHeight.Value : 0
                        ),
                        Mode = ResizeMode.Max,
                        Sampler = new BicubicResampler(),
                    }
                )
            );

            // The stored bytes must BE the resized image. Storing the original while the row
            // records the resized dimensions shipped blobs that disagreed with their metadata
            // — and meant the cap never actually shrank anything on disk. Re-encode in the
            // format the bytes arrived in; JPEG gets an explicit quality because the encoder
            // default (75) visibly degrades a photo that was only meant to be scaled down.
            var format =
                imageProcessor.Metadata.DecodedImageFormat
                ?? throw new InvalidOperationException("The decoded image reports no format.");
            var encoder =
                format is JpegFormat
                    ? new JpegEncoder { Quality = 90 }
                    : imageProcessor.Configuration.ImageFormatsManager.GetEncoder(format);
            using var resizedStream = new MemoryStream();
            await imageProcessor.SaveAsync(resizedStream, encoder);
            content = resizedStream.ToArray();
        }

        var image = new Image()
        {
            Extension = fileExtension,
            Name = fileNameWithoutExtension,
            ContentType = contentType,
            Height = imageProcessor.Height,
            Width = imageProcessor.Width,
        };

        // Routes to the filesystem store when enabled, otherwise the database — the router
        // stamps Size/StorageProvider and the content location on the image.
        await _storageRouter.Save(image, content, FileStorageTiers.Blob);
        _imageRepository.Add(image);
        return image;
    }

    /// <summary>
    /// Saves an image to the database and returns the image object. The db context is not saved.
    /// </summary>
    /// <param name="image">The image file.</param>
    public void DeleteImage(Image image)
    {
        if (image == null)
            return;
        _imageRepository.Delete(image);
    }
}
