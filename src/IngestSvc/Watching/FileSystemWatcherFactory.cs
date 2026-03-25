namespace IngestSvc.Watching;

public sealed class FileSystemWatcherFactory : IFileSystemWatcherFactory
{
    public FileSystemWatcher Create(string path) => new FileSystemWatcher(path);
}
