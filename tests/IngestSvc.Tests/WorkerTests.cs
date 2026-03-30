namespace IngestSvc.Tests;

public class WorkerTests
{
    [Fact]
    public async Task WaitForFileReady_Returns_When_File_Size_Is_Stable()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[1024]);

            await Worker.WaitForFileReadyAsync(path, maxAttempts: 3, delayMs: 50);
            // no exception = success
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WaitForFileReady_Throws_When_File_Is_Empty()
    {
        var path = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(
                () => Worker.WaitForFileReadyAsync(path, maxAttempts: 3, delayMs: 50));
        }
        finally
        {
            File.Delete(path);
        }
    }
}