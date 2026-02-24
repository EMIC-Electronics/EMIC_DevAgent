using System.Text.Json;
using EMIC.Shared.Services.Storage;
using EMIC_DevAgent.Core.Configuration;
using EMIC_DevAgent.Core.Models.Sdk;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Sdk;

public class SdkScanner : ISdkScanner
{
    private readonly MediaAccess _mediaAccess;
    private readonly IAgentSession _session;
    private readonly ILogger<SdkScanner> _logger;
    private SdkInventory? _cached;

    public SdkScanner(MediaAccess mediaAccess, IAgentSession session, ILogger<SdkScanner> logger)
    {
        _mediaAccess = mediaAccess;
        _session = session;
        _logger = logger;
    }

    public Task<SdkInventory> ScanAsync(string sdkPath, CancellationToken ct = default)
    {
        if (_cached != null)
            return Task.FromResult(_cached);

        _logger.LogInformation("Starting SDK scan for: {SdkPath}", sdkPath);

        var inventory = new SdkInventory
        {
            SdkRootPath = sdkPath,
            LastScanTime = DateTime.UtcNow
        };

        EnumerateApis(inventory);
        EnumerateDrivers(inventory);
        EnumerateModules(inventory);
        EnumerateHal(inventory);

        _logger.LogInformation(
            "SDK scan complete: {ApiCount} APIs, {DriverCount} drivers, {ModuleCount} modules, {HalCount} HAL components",
            inventory.Apis.Count, inventory.Drivers.Count, inventory.Modules.Count, inventory.HalComponents.Count);

        _cached = inventory;
        return Task.FromResult(inventory);
    }

    public async Task<ApiDefinition?> FindApiAsync(string name, CancellationToken ct = default)
    {
        var inventory = await ScanAsync(_session.SdkPath, ct);
        return inventory.Apis.FirstOrDefault(a =>
            a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<DriverDefinition?> FindDriverAsync(string name, CancellationToken ct = default)
    {
        var inventory = await ScanAsync(_session.SdkPath, ct);
        return inventory.Drivers.FirstOrDefault(d =>
            d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ModuleDefinition?> FindModuleAsync(string name, CancellationToken ct = default)
    {
        var inventory = await ScanAsync(_session.SdkPath, ct);
        return inventory.Modules.FirstOrDefault(m =>
            m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private void EnumerateApis(SdkInventory inv)
    {
        const string apiRoot = "DEV:/_api";
        if (!_mediaAccess.Directory.Exists(apiRoot)) return;

        foreach (var catPath in _mediaAccess.Directory.GetDirectories(apiRoot))
        {
            var category = GetLastSegment(catPath);
            foreach (var apiPath in _mediaAccess.Directory.GetDirectories(catPath))
            {
                var name = GetLastSegment(apiPath);
                var emicFile = $"{apiPath}/{name.ToLower()}.emic";
                inv.Apis.Add(new ApiDefinition
                {
                    Name = name,
                    Category = category,
                    EmicFilePath = _mediaAccess.File.Exists(emicFile) ? emicFile : string.Empty,
                    IncPath = $"{apiPath}/inc",
                    SrcPath = $"{apiPath}/src"
                });
            }
        }
    }

    private void EnumerateDrivers(SdkInventory inv)
    {
        const string driversRoot = "DEV:/_drivers";
        if (!_mediaAccess.Directory.Exists(driversRoot)) return;

        foreach (var catPath in _mediaAccess.Directory.GetDirectories(driversRoot))
        {
            var category = GetLastSegment(catPath);
            foreach (var driverPath in _mediaAccess.Directory.GetDirectories(catPath))
            {
                var name = GetLastSegment(driverPath);
                var emicFile = $"{driverPath}/{name.ToLower()}.emic";
                inv.Drivers.Add(new DriverDefinition
                {
                    Name = name,
                    Category = category,
                    EmicFilePath = _mediaAccess.File.Exists(emicFile) ? emicFile : string.Empty
                });
            }
        }
    }

    private void EnumerateModules(SdkInventory inv)
    {
        const string modulesRoot = "DEV:/_modules";
        if (!_mediaAccess.Directory.Exists(modulesRoot)) return;

        foreach (var catPath in _mediaAccess.Directory.GetDirectories(modulesRoot))
        {
            var category = GetLastSegment(catPath);
            foreach (var modulePath in _mediaAccess.Directory.GetDirectories(catPath))
            {
                var name = GetLastSegment(modulePath);
                var systemPath = $"{modulePath}/System";
                var targetPath = $"{modulePath}/Target";

                var mod = new ModuleDefinition
                {
                    Name = name,
                    Category = category,
                    GenerateEmicPath = _mediaAccess.File.Exists($"{systemPath}/generate.emic")
                        ? $"{systemPath}/generate.emic" : string.Empty,
                    DeployEmicPath = _mediaAccess.File.Exists($"{systemPath}/deploy.emic")
                        ? $"{systemPath}/deploy.emic" : string.Empty,
                    DescriptionJsonPath = _mediaAccess.File.Exists($"{modulePath}/m_description.json")
                        ? $"{modulePath}/m_description.json" : string.Empty
                };

                // Read m_description.json if available for extra metadata
                if (!string.IsNullOrEmpty(mod.DescriptionJsonPath))
                    TryReadModuleDescription(mod);

                inv.Modules.Add(mod);
            }
        }
    }

    private void EnumerateHal(SdkInventory inv)
    {
        const string halRoot = "DEV:/_hal";
        if (!_mediaAccess.Directory.Exists(halRoot)) return;

        foreach (var halPath in _mediaAccess.Directory.GetDirectories(halRoot))
        {
            var name = GetLastSegment(halPath);
            inv.HalComponents.Add(name);
        }
    }

    private void TryReadModuleDescription(ModuleDefinition mod)
    {
        try
        {
            var json = _mediaAccess.File.ReadAllText(mod.DescriptionJsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("board", out var board))
                mod.HardwareBoard = board.GetString() ?? string.Empty;

            if (root.TryGetProperty("apis", out var apis) && apis.ValueKind == JsonValueKind.Array)
            {
                foreach (var api in apis.EnumerateArray())
                {
                    var apiName = api.GetString();
                    if (!string.IsNullOrEmpty(apiName))
                        mod.RequiredApis.Add(apiName);
                }
            }

            if (root.TryGetProperty("drivers", out var drivers) && drivers.ValueKind == JsonValueKind.Array)
            {
                foreach (var driver in drivers.EnumerateArray())
                {
                    var driverName = driver.GetString();
                    if (!string.IsNullOrEmpty(driverName))
                        mod.RequiredDrivers.Add(driverName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read m_description.json for module {Name}", mod.Name);
        }
    }

    /// <summary>
    /// Gets the last path segment from a virtual path (e.g., "DEV:/_api/Comms" -> "Comms").
    /// Works with both '/' and '\' separators.
    /// </summary>
    private static string GetLastSegment(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var lastSlash = trimmed.LastIndexOfAny(new[] { '/', '\\' });
        return lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;
    }
}
