using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace IngestSvc.Watermarking;

public sealed class PhotoWatermarker : IPhotoWatermarker
{
    private readonly WatermarkOptions _options;
    private readonly ILogger<PhotoWatermarker> _logger;

    public PhotoWatermarker(IOptions<WatermarkOptions> options, ILogger<PhotoWatermarker> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Stream Apply(Stream input)
    {
        bool hasLeft = !string.IsNullOrWhiteSpace(_options.BottomLeftPath);
        bool hasRight = !string.IsNullOrWhiteSpace(_options.BottomRightPath);

        if (!hasLeft && !hasRight)
        {
            _logger.LogDebug("Watermark: no paths configured, skipping.");
            return input;
        }

        using var image = Image.Load(input);
        int pad = _options.PaddingPixels;

        _logger.LogDebug("Watermark: image {W}x{H}, pad={Pad}, heightPx={H2}", image.Width, image.Height, pad, _options.HeightPx);

        image.Mutate(ctx =>
        {
            if (hasLeft)
            {
                using var wm = LoadAndResize(_options.BottomLeftPath!);
                var point = new Point(pad, image.Height - wm.Height - pad);
                _logger.LogDebug("Watermark BL: wm={WW}x{WH} at ({X},{Y})", wm.Width, wm.Height, point.X, point.Y);
                ctx.DrawImage(wm, point, 1f);
            }

            if (hasRight)
            {
                using var wm = LoadAndResize(_options.BottomRightPath!);
                var point = new Point(image.Width - wm.Width - pad, image.Height - wm.Height - pad);
                _logger.LogDebug("Watermark BR: wm={WW}x{WH} at ({X},{Y})", wm.Width, wm.Height, point.X, point.Y);
                ctx.DrawImage(wm, point, 1f);
            }
        });

        var output = new MemoryStream();
        image.SaveAsJpeg(output);
        output.Seek(0, SeekOrigin.Begin);
        return output;
    }

    private Image LoadAndResize(string path)
    {
        var wm = Image.Load(path);

        if (_options.HeightPx <= 0 || wm.Height == _options.HeightPx)
            return wm;

        double ratio = (double)_options.HeightPx / wm.Height;
        int newWidth = (int)Math.Round(wm.Width * ratio);
        wm.Mutate(ctx => ctx.Resize(newWidth, _options.HeightPx));
        return wm;
    }
}
