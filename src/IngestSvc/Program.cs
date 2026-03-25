using IngestSvc;
using IngestSvc.Watching;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.Configure<WatcherOptions>(
    builder.Configuration.GetSection("Watcher")
);
builder.Services.AddSingleton<IFileSystemWatcherFactory, FileSystemWatcherFactory>();

var host = builder.Build();
host.Run();
