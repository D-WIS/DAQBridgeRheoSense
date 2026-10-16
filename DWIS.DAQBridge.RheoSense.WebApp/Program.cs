using DWIS.DAQBridge.RheoSense.WebApp;
using DWIS.DAQBridge.RheoSense.WebApp.Services;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
string homeDirectory = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "home"));
string dataProtectionDirectory = Path.Combine(homeDirectory, "data-protection-keys");
Directory.CreateDirectory(dataProtectionDirectory);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionDirectory))
    .SetApplicationName("DWIS.DAQBridge.RheoSense.WebApp");
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomLeft;
    config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
});
builder.Services.AddSingleton<RheoSenseHistoryStore>();
builder.Services.AddSingleton<RheoSensePreferenceStore>();
builder.Services.AddHostedService<BlackboardReaderService>();

var app = builder.Build();
app.UseForwardedHeaders();
app.UsePathBase(builder.Configuration["BasePath"] ?? "/RheoSense/webapp");
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");
app.Run();
