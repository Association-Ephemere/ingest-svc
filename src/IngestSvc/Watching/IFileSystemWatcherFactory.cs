namespace IngestSvc.Watching;

public interface IFileSystemWatcherFactory
{
    FileSystemWatcher Create(string path);
}
