using DWIS.DAQBridge.RheoSense.OPCUASource;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
