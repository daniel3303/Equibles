using File = Equibles.Media.Data.Models.File;

namespace Equibles.Media.BusinessLogic;

public interface IFileManager {
    public Task<File> SaveFile(byte[] content, string fileName, bool protect = false);
    public void DeleteFile(File file);
}