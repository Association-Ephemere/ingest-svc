namespace IngestSvc.Resizing;

public interface IPhotoResizer
{
    Stream Resize(Stream input);
}
