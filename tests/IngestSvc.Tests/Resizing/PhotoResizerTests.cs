using IngestSvc.Resizing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace IngestSvc.Tests.Resizing;

public class PhotoResizerTests
{
    private static IPhotoResizer CreateResizer(int maxWidth, int maxHeight) =>
        new PhotoResizer(
            Options.Create(new ResizeOptions { MaxWidth = maxWidth, MaxHeight = maxHeight }),
            NullLogger<PhotoResizer>.Instance
        );

    private static Stream CreateJpegStream(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height);
        var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }

    [Fact]
    public void Resize_ReducesLargerImageToFitWithinMaxDimensions()
    {
        var resizer = CreateResizer(maxWidth: 100, maxHeight: 100);
        using var input = CreateJpegStream(400, 300);

        using var output = resizer.Resize(input);

        using var result = Image.Load(output);
        Assert.True(result.Width <= 100);
        Assert.True(result.Height <= 100);
    }

    [Fact]
    public void Resize_PreservesAspectRatio()
    {
        // 400x200 (2:1) -> max 100x100 -> expect 100x50
        var resizer = CreateResizer(maxWidth: 100, maxHeight: 100);
        using var input = CreateJpegStream(400, 200);

        using var output = resizer.Resize(input);

        using var result = Image.Load(output);
        Assert.Equal(100, result.Width);
        Assert.Equal(50, result.Height);
    }

    [Fact]
    public void Resize_DoesNotUpscaleImageAlreadySmallerThanMax()
    {
        var resizer = CreateResizer(maxWidth: 800, maxHeight: 800);
        using var input = CreateJpegStream(100, 75);

        using var output = resizer.Resize(input);

        using var result = Image.Load(output);
        Assert.Equal(100, result.Width);
        Assert.Equal(75, result.Height);
    }

    [Fact]
    public void Resize_ReturnsReadableStream()
    {
        var resizer = CreateResizer(maxWidth: 100, maxHeight: 100);
        using var input = CreateJpegStream(200, 200);

        var output = resizer.Resize(input);

        Assert.True(output.CanRead);
        Assert.Equal(0, output.Position);
        output.Dispose();
    }
}
