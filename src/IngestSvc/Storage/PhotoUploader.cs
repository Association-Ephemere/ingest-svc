using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace IngestSvc.Storage;

public sealed partial class PhotoUploader : IPhotoUploader
{
    private readonly IMinioClient _client;
    private readonly StorageOptions _options;
    private readonly ILogger<PhotoUploader> _logger;

    public PhotoUploader(
        IMinioClient client,
        IOptions<StorageOptions> options,
        ILogger<PhotoUploader> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            var args = new BucketExistsArgs().WithBucket(_options.Bucket);
            bool exists = await _client.BucketExistsAsync(args, ct);
            if (!exists)
            {
                LogBucketMissing(_logger, _options.Bucket);
                throw new InvalidOperationException($"Bucket '{_options.Bucket}' missing.");
            }
        }, ct);
        LogBucketReady(_logger, _options.Bucket);
    }

    public async Task UploadAsync(string key, Stream fullRes, Stream lowRes, CancellationToken ct = default)
    {
        await UploadOneAsync($"{_options.FullPrefix}/{key}", fullRes, ct);
        await UploadOneAsync($"{_options.LowPrefix}/{key}", lowRes, ct);
    }

    private async Task UploadOneAsync(string objectName, Stream stream, CancellationToken ct)
    {
        LogUploadStarted(_logger, objectName, _options.Bucket);
        await ExecuteWithRetryAsync(async () =>
        {
            var args = new PutObjectArgs()
                .WithBucket(_options.Bucket)
                .WithObject(objectName)
                .WithStreamData(stream)
                .WithObjectSize(stream.Length)
                .WithContentType("image/jpeg");

            await _client.PutObjectAsync(args, ct);
        }, ct);
        LogUploadCompleted(_logger, objectName, _options.Bucket);
    }

    private async Task ExecuteWithRetryAsync(Func<Task> action, CancellationToken ct)
    {
        int delayMs = _options.RetryInitialDelayMs;
        int attempt = 0;

        while (true)
        {
            try
            {
                await action();
                if (attempt > 0)
                    LogConnectionRestored(_logger, attempt);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && IsRetriable(ex))
            {
                attempt++;
                if (attempt == 1)
                    LogConnectionLost(_logger);
                LogOperationRetry(_logger, ex, attempt, delayMs);
                await Task.Delay(delayMs, ct);

                delayMs = Math.Min(delayMs * 2, _options.RetryMaxDelayMs);
            }
        }
    }

    private static bool IsRetriable(Exception ex)
    {
        if (ex is HttpRequestException || ex is TimeoutException || ex is IOException || ex is InvalidOperationException)
            return true;

        if (ex is Minio.Exceptions.MinioException)
        {
            var name = ex.GetType().Name;
            if (name == "AuthorizationException" ||
                name == "InvalidBucketNameException" ||
                name == "AccessDeniedException")
            {
                return false;
            }
            return true;
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bucket {Bucket} does not exist yet. Please create it.")]
    private static partial void LogBucketMissing(ILogger logger, string bucket);

    [LoggerMessage(Level = LogLevel.Information, Message = "MinIO bucket {Bucket} is ready.")]
    private static partial void LogBucketReady(ILogger logger, string bucket);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Upload started: {ObjectName} -> bucket {Bucket}")]
    private static partial void LogUploadStarted(ILogger logger, string objectName, string bucket);

    [LoggerMessage(Level = LogLevel.Information, Message = "Upload completed: {ObjectName} -> bucket {Bucket}")]
    private static partial void LogUploadCompleted(ILogger logger, string objectName, string bucket);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MinIO connection lost.")]
    private static partial void LogConnectionLost(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MinIO operation failed (attempt {Attempt}). Retrying in {DelayMs} ms...")]
    private static partial void LogOperationRetry(ILogger logger, Exception ex, int attempt, int delayMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "MinIO connection restored after {Attempt} attempt(s).")]
    private static partial void LogConnectionRestored(ILogger logger, int attempt);
}
