using Equibles.Core.AutoWiring;
using Equibles.Media.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Media.BusinessLogic.Storage;

/// <summary>
/// Stores bytes inline in the FileContent table — the original behavior. This is the
/// default backend, so existing rows and callers are unchanged.
/// </summary>
[Service(ServiceLifetime.Scoped)]
public class DatabaseFileStorageProvider : IFileStorageProvider
{
    public StorageProvider Provider => StorageProvider.Database;

    public Task Save(File file, byte[] content, string tier)
    {
        file.Size = content.Length;
        file.StorageProvider = StorageProvider.Database;
        file.FileContent = new FileContent() { File = file, Bytes = content };
        return Task.CompletedTask;
    }

    public Task<byte[]> GetContent(File file)
    {
        // Null-tolerant: a File indexed but never downloaded has a FileContent row with
        // null Bytes, and callers treat null as "no content" — matching the pre-refactor
        // `file.Content?.FileContent?.Bytes` semantics exactly.
        return Task.FromResult(file.FileContent?.Bytes);
    }

    public Task<Stream> OpenRead(File file)
    {
        return Task.FromResult<Stream>(new MemoryStream(file.FileContent.Bytes, writable: false));
    }
}
