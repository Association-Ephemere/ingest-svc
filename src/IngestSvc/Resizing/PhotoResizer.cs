using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace IngestSvc.Resizing;

public sealed partial class PhotoResizer : IPhotoResizer
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
        LogResizeStarted(_logger, _options.MaxWidth, _options.MaxHeight);
        try
        {
            using var image = Image.Load(input);

            var (newWidth, newHeight) = CalculateDimensions(image.Width, image.Height);

            if (newWidth < image.Width || newHeight < image.Height)
                image.Mutate(ctx => ctx.Resize(newWidth, newHeight));

            var output = new MemoryStream();
            image.SaveAsJpeg(output);
            output.Seek(0, SeekOrigin.Begin);
            LogResizeCompleted(_logger, newWidth, newHeight);
            return output;
        }
        catch (Exception ex)
        {
            LogResizeFailed(_logger, ex, _options.MaxWidth, _options.MaxHeight);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Resize started (max {MaxWidth}x{MaxHeight})")]
    private static partial void LogResizeStarted(ILogger logger, int maxWidth, int maxHeight);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Resize completed: output {Width}x{Height}")]
    private static partial void LogResizeCompleted(ILogger logger, int width, int height);

    [LoggerMessage(Level = LogLevel.Error, Message = "Resize failed (max {MaxWidth}x{MaxHeight})")]
    private static partial void LogResizeFailed(ILogger logger, Exception ex, int maxWidth, int maxHeight);
}
