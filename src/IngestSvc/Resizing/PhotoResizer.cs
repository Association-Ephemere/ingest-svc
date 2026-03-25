using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace IngestSvc.Resizing;

public sealed class PhotoResizer : IPhotoResizer
{
    private readonly ResizeOptions _options;
    private readonly ILogger<PhotoResizer> _logger;

    public PhotoResizer(IOptions<ResizeOptions> options, ILogger<PhotoResizer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Stream Resize(Stream input)
    {
        try
        {
            using var image = Image.Load(input);

            var (newWidth, newHeight) = CalculateDimensions(image.Width, image.Height);

            if (newWidth < image.Width || newHeight < image.Height)
                image.Mutate(ctx => ctx.Resize(newWidth, newHeight));

            var output = new MemoryStream();
            image.SaveAsJpeg(output);
            output.Seek(0, SeekOrigin.Begin);
            return output;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resize image (MaxWidth={MaxWidth}, MaxHeight={MaxHeight})",
                _options.MaxWidth, _options.MaxHeight);
            throw;
        }
    }

    private (int width, int height) CalculateDimensions(int originalWidth, int originalHeight)
    {
        if (originalWidth <= _options.MaxWidth && originalHeight <= _options.MaxHeight)
            return (originalWidth, originalHeight);

        var widthRatio = (double)_options.MaxWidth / originalWidth;
        var heightRatio = (double)_options.MaxHeight / originalHeight;
        var ratio = Math.Min(widthRatio, heightRatio);

        return ((int)(originalWidth * ratio), (int)(originalHeight * ratio));
    }
}
