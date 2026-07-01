using Equibles.Core.Extensions;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Repositories;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Owns a document's as-filed image set: it stores the images captured from EDGAR as
/// <see cref="DocumentImage"/> rows (each backed by a Media <c>File</c> blob) and clears the
/// prior set first so a re-stitch (builder-version bump) replaces images instead of accumulating
/// orphans. The viewer serves these from our own origin so a filing's slide deck / logo renders
/// without hotlinking SEC.
/// </summary>
public class DocumentImageService
{
    // Media.File.Name is capped at 256 and Extension at 16; EDGAR names are short bare tokens but
    // guard against a pathological value so the insert can never overflow.
    private const int MaxFileNameLength = 256;
    private const int MaxExtensionLength = 16;

    private readonly DocumentImageRepository _documentImageRepository;
    private readonly FileRepository _fileRepository;
    private readonly IFileManager _fileManager;

    public DocumentImageService(
        DocumentImageRepository documentImageRepository,
        FileRepository fileRepository,
        IFileManager fileManager
    )
    {
        _documentImageRepository = documentImageRepository;
        _fileRepository = fileRepository;
        _fileManager = fileManager;
    }

    /// <summary>
    /// Reconciles the document's stored images with the freshly captured set: drops the prior
    /// images (and their blobs) then stores the new ones. Does NOT call SaveChanges — the caller
    /// commits the tracked inserts in the same unit of work. The prior-image clear is an immediate
    /// SQL delete so a re-stitch can reuse the same filenames without colliding on the unique index.
    /// </summary>
    public async Task SyncImages(
        Document document,
        IReadOnlyList<CapturedImage> images,
        CancellationToken cancellationToken = default
    )
    {
        await ClearImages(document, cancellationToken);

        foreach (var image in images)
        {
            var file = await _fileManager.SaveInternalFile(
                image.Bytes,
                FileNameWithoutExtension(image.FileName).TruncateToFit(MaxFileNameLength),
                Extension(image.FileName).TruncateToFit(MaxExtensionLength),
                image.ContentType,
                storage: Equibles.Media.Data.Models.StorageProvider.FileSystem
            );

            _documentImageRepository.Add(
                new DocumentImage
                {
                    Document = document,
                    // Stored verbatim (the stitcher bounds the name to the column length) so the key
                    // equals the bare filename the viewer parses from the as-filed HTML to match it.
                    FileName = image.FileName,
                    File = file,
                }
            );
        }
    }

    // Removes the document's existing images and their blobs. The link rows go first (the File FK
    // is restrict, so a blob can't be deleted while a link still references it); the File delete
    // cascades to its FileContent at the DB level. Skips the writes entirely when none exist (the
    // common case on first capture).
    private async Task ClearImages(Document document, CancellationToken cancellationToken)
    {
        var fileIds = await _documentImageRepository
            .GetAll()
            .Where(di => di.DocumentId == document.Id)
            .Select(di => di.FileId)
            .ToListAsync(cancellationToken);

        if (fileIds.Count == 0)
            return;

        await _documentImageRepository
            .GetAll()
            .Where(di => di.DocumentId == document.Id)
            .ExecuteDeleteAsync(cancellationToken);

        await _fileRepository
            .GetAll()
            .Where(f => fileIds.Contains(f.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    // The bare extension (no dot) of an EDGAR filename, e.g. "jpg" from "deck001.jpg". Empty when
    // the name carries none.
    private static string Extension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot >= 0 && dot < fileName.Length - 1 ? fileName[(dot + 1)..] : string.Empty;
    }

    private static string FileNameWithoutExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }
}
