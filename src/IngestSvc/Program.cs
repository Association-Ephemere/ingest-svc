using IngestSvc;
using IngestSvc.Naming;
using IngestSvc.Resizing;
using IngestSvc.Watching;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.Configure<WatcherOptions>(
    builder.Configuration.GetSection("Watcher")
);
builder.Services.Configure<ResizeOptions>(
    builder.Configuration.GetSection("Resize")
);
builder.Services.AddSingleton<IFileSystemWatcherFactory, FileSystemWatcherFactory>();
builder.Services.AddSingleton<IPhotoNamer, PhotoNamer>();
builder.Services.AddSingleton<IPhotoResizer, PhotoResizer>();

var host = builder.Build();
host.Run();
