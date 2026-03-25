using IngestSvc;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.Configure<WatcherOptions>(
    builder.Configuration.GetSection("Watcher")
);

var host = builder.Build();
host.Run();
