using System.Text.Json;
using System.Text.Json.Nodes;
using DWIS.DAQBridge.RheoSense.Model;

namespace DWIS.DAQBridge.RheoSense.WebApp.Services;

public sealed class RheoSensePreferenceStore
{
    public const int MinimumHours = 1;
    public const int MaximumHours = 48;
    private readonly string _configurationPath;
    private readonly ILogger<RheoSensePreferenceStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RheoSensePreferenceStore(IWebHostEnvironment environment, ILogger<RheoSensePreferenceStore> logger)
    {
        _configurationPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "home", "config.json"));
        _logger = logger;
    }

    public int LoadTimeSpanHours()
    {
        try
        {
            if (File.Exists(_configurationPath))
            {
                ConfigurationForRheoSense? configuration = JsonSerializer.Deserialize<ConfigurationForRheoSense>(File.ReadAllText(_configurationPath));
                if (configuration is not null)
                {
                    return Math.Clamp(configuration.WebAppTimeSpanHours, MinimumHours, MaximumHours);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the RheoSense WebApp time-span preference.");
        }

        return 6;
    }

    public async Task SaveTimeSpanHoursAsync(int hours)
    {
        hours = Math.Clamp(hours, MinimumHours, MaximumHours);
        await _gate.WaitAsync();
        try
        {
            string? directory = Path.GetDirectoryName(_configurationPath);
            Directory.CreateDirectory(directory!);
            JsonObject root = File.Exists(_configurationPath)
                ? JsonNode.Parse(await File.ReadAllTextAsync(_configurationPath))?.AsObject() ?? new JsonObject()
                : JsonSerializer.SerializeToNode(new ConfigurationForRheoSense())!.AsObject();
            root[nameof(ConfigurationForRheoSense.WebAppTimeSpanHours)] = hours;

            string temporaryPath = _configurationPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporaryPath, _configurationPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
