using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Sdk;
using EMIC_DevAgent.Core.Services.Sdk;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents;

/// <summary>
/// Escanea el SDK (_api, _drivers, _modules, _hal), encuentra componentes
/// reutilizables e identifica gaps que necesitan ser creados.
/// </summary>
public class AnalyzerAgent : AgentBase
{
    private readonly ISdkScanner _sdkScanner;

    public AnalyzerAgent(ISdkScanner sdkScanner, ILogger<AnalyzerAgent> logger)
        : base(logger)
    {
        _sdkScanner = sdkScanner;
    }

    public override string Name => "Analyzer";
    public override string Description => "Escanea SDK, encuentra componentes reutilizables, identifica gaps";

    public override bool CanHandle(AgentContext context)
        => context.Analysis != null;

    protected override async Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        var analysis = context.Analysis!;
        Logger.LogInformation("Analyzing SDK for intent: {Intent}, component: {Component}",
            analysis.Intent, analysis.ComponentName);

        // 1. Scan SDK to build inventory
        var sdkPath = context.Properties.TryGetValue("SdkPath", out var path)
            ? path.ToString() ?? string.Empty
            : string.Empty;

        SdkInventory inventory;
        try
        {
            inventory = await _sdkScanner.ScanAsync(sdkPath, ct);
            context.SdkState = inventory;
            Logger.LogInformation("SDK scan complete: {Apis} APIs, {Drivers} drivers, {Modules} modules, {Hal} HAL components",
                inventory.Apis.Count, inventory.Drivers.Count, inventory.Modules.Count, inventory.HalComponents.Count);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SDK scan failed, proceeding with empty inventory");
            inventory = new SdkInventory();
            context.SdkState = inventory;
        }

        // 2. Find reusable components based on intent
        var reusableApis = FindMatchingApis(inventory, analysis);
        var reusableDrivers = FindMatchingDrivers(inventory, analysis);
        var reusableModules = FindMatchingModules(inventory, analysis);

        // 3. Identify what needs to be created
        var gaps = new List<string>();

        switch (analysis.Intent)
        {
            case IntentType.CreateApi:
                if (!reusableApis.Any(a => a.Name.Equals(analysis.ComponentName, StringComparison.OrdinalIgnoreCase)))
                    gaps.Add($"API: {analysis.ComponentName}");
                break;

            case IntentType.CreateDriver:
                if (!reusableDrivers.Any(d => d.Name.Equals(analysis.ComponentName, StringComparison.OrdinalIgnoreCase)))
                    gaps.Add($"Driver: {analysis.ComponentName}");
                break;

            case IntentType.CreateModule:
                if (!reusableModules.Any(m => m.Name.Equals(analysis.ComponentName, StringComparison.OrdinalIgnoreCase)))
                    gaps.Add($"Module: {analysis.ComponentName}");
                // Check if required APIs/drivers exist
                foreach (var dep in analysis.RequiredDependencies)
                {
                    var apiExists = inventory.Apis.Any(a => a.Name.Equals(dep, StringComparison.OrdinalIgnoreCase));
                    var driverExists = inventory.Drivers.Any(d => d.Name.Equals(dep, StringComparison.OrdinalIgnoreCase));
                    if (!apiExists && !driverExists)
                        gaps.Add($"Dependency: {dep}");
                }
                break;
        }

        // 4. Check driver interchangeability (drivers in same category should have matching function names)
        var interchangeabilityIssues = CheckDriverInterchangeability(inventory);

        // 5. Store results in context properties
        context.Properties["ReusableApis"] = reusableApis;
        context.Properties["ReusableDrivers"] = reusableDrivers;
        context.Properties["ReusableModules"] = reusableModules;
        context.Properties["Gaps"] = gaps;

        if (interchangeabilityIssues.Count > 0)
            context.Properties["InterchangeabilityIssues"] = interchangeabilityIssues;

        var message = gaps.Count > 0
            ? $"Analysis complete. {gaps.Count} component(s) need to be created: {string.Join(", ", gaps)}"
            : $"Analysis complete. All required components found in SDK.";

        if (interchangeabilityIssues.Count > 0)
            message += $" ({interchangeabilityIssues.Count} interchangeability warnings)";

        Logger.LogInformation(message);
        return AgentResult.Success(Name, message);
    }

    private static List<ApiDefinition> FindMatchingApis(SdkInventory inventory, PromptAnalysis analysis)
    {
        return inventory.Apis
            .Where(a => a.Name.Contains(analysis.ComponentName, StringComparison.OrdinalIgnoreCase) ||
                        a.Category.Contains(analysis.Category, StringComparison.OrdinalIgnoreCase) ||
                        analysis.RequiredDependencies.Any(d => a.Name.Equals(d, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static List<DriverDefinition> FindMatchingDrivers(SdkInventory inventory, PromptAnalysis analysis)
    {
        return inventory.Drivers
            .Where(d => d.Name.Contains(analysis.ComponentName, StringComparison.OrdinalIgnoreCase) ||
                        d.Category.Contains(analysis.Category, StringComparison.OrdinalIgnoreCase) ||
                        analysis.RequiredDependencies.Any(dep => d.Name.Equals(dep, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static List<ModuleDefinition> FindMatchingModules(SdkInventory inventory, PromptAnalysis analysis)
    {
        return inventory.Modules
            .Where(m => m.Name.Contains(analysis.ComponentName, StringComparison.OrdinalIgnoreCase) ||
                        m.Category.Contains(analysis.Category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<string> CheckDriverInterchangeability(SdkInventory inventory)
    {
        var issues = new List<string>();

        // Group drivers by category
        var driversByCategory = inventory.Drivers
            .GroupBy(d => d.Category, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in driversByCategory)
        {
            var allFunctions = group
                .Select(d => new HashSet<string>(d.Functions, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (allFunctions.Count < 2) continue;

            // Check that all drivers in same category have the same function names
            var reference = allFunctions[0];
            for (int i = 1; i < allFunctions.Count; i++)
            {
                var missing = reference.Except(allFunctions[i]).ToList();
                var extra = allFunctions[i].Except(reference).ToList();

                if (missing.Count > 0 || extra.Count > 0)
                {
                    var driverNames = group.Select(d => d.Name).ToList();
                    issues.Add($"Category '{group.Key}': drivers {driverNames[0]} and {driverNames[i]} " +
                               $"have different function signatures (missing: [{string.Join(", ", missing)}], extra: [{string.Join(", ", extra)}])");
                }
            }
        }

        return issues;
    }
}
