namespace IngestSvc.Tests;

public class WorkerTests
{
    [Fact]
    public async Task WaitForFileReady_Returns_When_File_Is_Available()
    {
        var path = Path.GetTempFileName();
        try
        {
            await Worker.WaitForFileReadyAsync(path);
            // no exception = succes
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WaitForFileReady_Retries_When_File_Is_Locked()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var task = Worker.WaitForFileReadyAsync(path, maxAttempts: 3, delayMs: 50);

            await Task.Delay(100);
            stream.Close();

            await task;
        }
        finally
        {
            File.Delete(path);
        }
    }
}