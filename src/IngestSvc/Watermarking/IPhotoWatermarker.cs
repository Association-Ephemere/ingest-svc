namespace IngestSvc.Watermarking;

public interface IPhotoWatermarker
{
    Stream Apply(Stream input);
}
