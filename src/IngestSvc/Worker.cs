using System.Collections.Concurrent;
using IngestSvc.Naming;
using IngestSvc.Resizing;
using IngestSvc.Storage;
using IngestSvc.Watching;
using Microsoft.Extensions.Options;

namespace IngestSvc;

public partial class Worker : BackgroundService
{
    private readonly ConcurrentDictionary<string, byte> _processingFiles = new();
    private readonly ILogger<Worker> _logger;
    private readonly IOptions<WatcherOptions> _options;
    private readonly IFileSystemWatcherFactory _factory;
    private readonly IPhotoNamer _namer;
    private readonly IPhotoResizer _resizer;
    private readonly IPhotoUploader _uploader;
    private readonly SemaphoreSlim _semaphore = new(4);
    private readonly ConcurrentDictionary<Task, byte> _activeTasks = new();
    private readonly object _lock = new();
    private bool _isShuttingDown = false;
    private CancellationToken _stoppingToken;

    public Worker(
        ILogger<Worker> logger,
        IOptions<WatcherOptions> options,
        IFileSystemWatcherFactory factory,
        IPhotoNamer namer,
        IPhotoResizer resizer,
        IPhotoUploader uploader)
    {
        _logger = logger;
        _options = options;
        _factory = factory;
        _namer = namer;
        _resizer = resizer;
        _uploader = uploader;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        if (!string.IsNullOrWhiteSpace(_options.Value.Path))
        {
            Directory.CreateDirectory(_options.Value.Path);
        }

        if (!string.IsNullOrWhiteSpace(_options.Value.ProcessedPath))
        {
            Directory.CreateDirectory(_options.Value.ProcessedPath);
        }

        if (!string.IsNullOrWhiteSpace(_options.Value.FailedPath))
        {
            Directory.CreateDirectory(_options.Value.FailedPath);
        }

        LogWaitingForMinio(_logger);
        try
        {
            await _uploader.EnsureReadyAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            LogMinioReadinessFailure(_logger, ex);
            throw;
        }

        using var watcher = _factory.Create(_options.Value.Path);
        watcher.Filter = "*";
        watcher.NotifyFilter = NotifyFilters.FileName;
        watcher.InternalBufferSize = 65536; // Handle huge bursts up to ~1000 files
        watcher.Created += OnFileCreated;
        watcher.EnableRaisingEvents = true;

        LogWatching(_logger, _options.Value.Path);

        // Sweep periodically in the background for any missed events or timeouts
        var sweepTask = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
                SweepDirectory();

                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    SweepDirectory();
                }
            }
            catch (OperationCanceledException) { }
        });

        try
        {
            var completedTask = await Task.WhenAny(sweepTask, Task.Delay(Timeout.Infinite, stoppingToken));
            await completedTask; // bubbles up Faults
        }
        catch (OperationCanceledException)
        {
            // Allowed to throw during cancellation
        }

        if (stoppingToken.IsCancellationRequested)
        {
            LogShutdownInitiated(_logger);

            lock (_lock)
            {
                _isShuttingDown = true;
            }

            watcher.EnableRaisingEvents = false;

            await sweepTask; // Ensure loop terminates completely

            var pendingTasks = _activeTasks.Keys.ToList();
            if (pendingTasks.Any())
            {
                LogWaitingForPending(_logger, pendingTasks.Count);
                await Task.WhenAll(pendingTasks);
            }

            LogShutdownComplete(_logger);
        }
    }

    private void SweepDirectory()
    {
        try
        {
            foreach (var existingFile in Directory.GetFiles(_options.Value.Path)
                     .Where(f => SupportedExtensions.Contains(Path.GetExtension(f))))
            {
                lock (_lock)
                {
                    if (_isShuttingDown) return;
                    var task = Task.Run(() => ProcessSafeAsync(existingFile, _stoppingToken));
                    _activeTasks.TryAdd(task, 0);
                    _ = task.ContinueWith(t => _activeTasks.TryRemove(t, out _));
                }
            }
        }
        catch (Exception ex)
        {
            LogSweepFailed(_logger, ex);
        }
    }

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!SupportedExtensions.Contains(Path.GetExtension(e.FullPath)))
            return;
        lock (_lock)
        {
            if (_isShuttingDown) return;
            var task = Task.Run(() => ProcessSafeAsync(e.FullPath, _stoppingToken));
            _activeTasks.TryAdd(task, 0);
            _ = task.ContinueWith(t => _activeTasks.TryRemove(t, out _));
        }
    }

    private async Task ProcessSafeAsync(string fullPath, CancellationToken token)
    {
        if (!_processingFiles.TryAdd(fullPath, 0))
            return; // Already being processed by sweep or watcher

        try
        {
            await ProcessFileAsync(fullPath, token);
        }
        catch (OperationCanceledException)
        {
            LogProcessingCancelled(_logger, fullPath);
        }
        catch (Exception ex)
        {
            LogProcessingError(_logger, ex, fullPath);
        }
        finally
        {
            _processingFiles.TryRemove(fullPath, out _);
        }
    }

    internal async Task ProcessFileAsync(string fullPath, CancellationToken token = default)
    {
        await WaitForFileReadyAsync(fullPath, token);
        var key = _namer.Generate();
        LogFileDetected(_logger, fullPath, key);

        bool uploadSuccess = false;

        await _semaphore.WaitAsync(token);
        try
        {
            using (var file = File.OpenRead(fullPath))
            {
                using var fullRes = new MemoryStream();
                await file.CopyToAsync(fullRes);

                using var forResize = new MemoryStream(fullRes.ToArray());
                using var lowRes = _resizer.Resize(forResize);

                fullRes.Seek(0, SeekOrigin.Begin);
                await _uploader.UploadAsync(key, fullRes, lowRes);
                uploadSuccess = true;
            }
        }
        catch (Exception ex)
        {
            LogProcessingFailed(_logger, ex, fullPath);
        }
        finally
        {
            _semaphore.Release();
        }

        var destFolder = uploadSuccess ? _options.Value.ProcessedPath : _options.Value.FailedPath;

        if (string.IsNullOrWhiteSpace(destFolder))
        {
            return;
        }

        try
        {
            var destPath = Path.Combine(destFolder, Path.GetFileName(fullPath));
            File.Move(fullPath, destPath, overwrite: true);

            if (uploadSuccess)
                LogFileMovedToProcessed(_logger, fullPath);
            else
                LogFileMovedToFailed(_logger, fullPath);
        }
        catch (Exception ex)
        {
            LogFileMoveError(_logger, ex, fullPath, destFolder);
        }
    }

    internal static async Task WaitForFileReadyAsync(
        string path,
        CancellationToken cancellationToken = default,
        int maxAttempts = 240, // 2 minutes timeout to support massive parallel copying
        int delayMs = 500)
    {
        long previousSize = -1;

        for (int i = 0; i < maxAttempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                long currentSize = new FileInfo(path).Length;

                if (currentSize > 0 && currentSize == previousSize)
                {
                    using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    return;
                }

                previousSize = currentSize;
            }
            catch (IOException)
            {
                // File not yet accessible
            }
            catch (UnauthorizedAccessException)
            {
                // File permissions being set
            }

            await Task.Delay(delayMs, cancellationToken);
        }

        throw new TimeoutException(
            $"File not ready after {maxAttempts * delayMs / 1000}s: {path}");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for MinIO to become ready...")]
    private static partial void LogWaitingForMinio(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Failed to ensure MinIO readiness. Shutting down worker.")]
    private static partial void LogMinioReadinessFailure(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Watching {Path}")]
    private static partial void LogWatching(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shutdown initiated (SIGTERM received).")]
    private static partial void LogShutdownInitiated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for {Count} pending file(s) to complete...")]
    private static partial void LogWaitingForPending(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shutdown complete.")]
    private static partial void LogShutdownComplete(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Directory sweep failed.")]
    private static partial void LogSweepFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing cancelled for {Path} due to shutdown.")]
    private static partial void LogProcessingCancelled(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error processing {Path}.")]
    private static partial void LogProcessingError(ILogger logger, Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "File detected: {Path} -> key {Key}")]
    private static partial void LogFileDetected(ILogger logger, string path, string key);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process {Path}.")]
    private static partial void LogProcessingFailed(ILogger logger, Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "File moved to processed: {Path}")]
    private static partial void LogFileMovedToProcessed(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "File moved to failed: {Path}")]
    private static partial void LogFileMovedToFailed(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to move {Path} to {Destination}.")]
    private static partial void LogFileMoveError(ILogger logger, Exception ex, string path, string destination);
}
