namespace IngestSvc.Watermarking;

public sealed class WatermarkOptions
{
    public string? BottomLeftPath { get; set; }
    public string? BottomRightPath { get; set; }
    public int PaddingX { get; set; } = 20;
    public int PaddingY { get; set; } = 20;
    /// <summary>Target height of the watermark in pixels. Width scales proportionally. 0 = no resize.</summary>
    public int HeightPx { get; set; } = 0;
}
