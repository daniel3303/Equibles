using Equibles.Core.AutoWiring;
using Equibles.Media.Data.Models;
using Equibles.Media.Repositories;
using Microsoft.Extensions.DependencyInjection;
using MimeTypes;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Media.BusinessLogic;

[Service(ServiceLifetime.Scoped, typeof(IFileManager))]
public class FileManager : IFileManager {
    public static readonly IList<string> AcceptedExtensions = ["pdf", "png", "jpg", "jpeg", "xls", "xlsx", "doc", "docx", "txt", "psd"];
    public static string AcceptedExtensionsString() {
        return string.Concat(".", string.Join(",.", AcceptedExtensions));
    }

    private readonly FileRepository _fileRepository;

    public FileManager(FileRepository fileRepository) {
        _fileRepository = fileRepository;
    }

    /**
     * <summary>
     * Saves a file to the database. The db context is not saved.
     * The file name is used to infer the file extension.
     * </summary>
     * <param name="content">The file content</param>
     * <param name="fileName">The file name</param>
     * <param name="protect">If the file should be protected using a security token for access</param>
     */
    public Task<File> SaveFile(byte[] content, string fileName, bool protect = false) {
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

        var file = new File() {
            Extension = fileExtension,
            Name = fileNameWithoutExtension,
            Size = content.Length,
            ContentType = contentType,
        };

        file.FileContent = new FileContent() {
            File = file,
            Bytes = content,
        };


        _fileRepository.Add(file);
        return Task.FromResult(file);
    }

    /// <summary>
    /// Deletes a file from the database. The db context is not saved.
    /// </summary>
    /// <param name="file">The file to delete</param>
    public void DeleteFile(File file) {
        if (file == null) return;
        _fileRepository.Delete(file);
    }
}