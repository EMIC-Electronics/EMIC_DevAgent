using EMIC_DevAgent.Core.Agents;
using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Configuration;
using EMIC_DevAgent.Core.Services.Compilation;
using EMIC_DevAgent.Core.Services.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Cli;

public class Program
{
    public static async Task Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var config = new EmicAgentConfig();
        configuration.GetSection("EmicAgent").Bind(config);
        configuration.GetSection("Llm").Bind(config.Llm);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        // Core services (includes ITemplateEngine registration)
        services.AddEmicDevAgent(config);

        // CLI-specific: host-provided implementations
        services.AddHttpClient<ClaudeLlmService>();
        services.AddScoped<ILlmService>(sp => sp.GetRequiredService<ClaudeLlmService>());
        services.AddSingleton<IUserInteraction, ConsoleUserInteraction>();
        services.AddSingleton<IAgentSession, CliAgentSession>();
        services.AddSingleton<IAgentEventSink, ConsoleEventSink>();

        // Compilation service
        services.AddScoped<ICompilationService, EmicCompilationService>();

        var provider = services.BuildServiceProvider();

        var logger = provider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("EMIC DevAgent v0.2.0 - Agente de IA para desarrollo SDK EMIC");

        Console.WriteLine("EMIC DevAgent v0.2.0");
        Console.WriteLine("====================");
        Console.WriteLine("Agente de IA para desarrollo SDK EMIC");
        Console.WriteLine();

        if (string.IsNullOrEmpty(config.SdkPath))
        {
            Console.WriteLine("ERROR: SdkPath no configurado en appsettings.json");
            Console.WriteLine("Configure la ruta al SDK EMIC en la propiedad EmicAgent.SdkPath");
            return;
        }

        Console.WriteLine($"SDK Path: {config.SdkPath}");
        Console.WriteLine($"Microcontrolador: {config.DefaultMicrocontroller}");
        Console.WriteLine($"LLM: {config.Llm.Provider} ({config.Llm.Model})");
        Console.WriteLine();
        Console.Write("Describa que desea crear > ");

        var prompt = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Console.WriteLine("No se ingreso ningun prompt. Saliendo.");
            return;
        }

        using var scope = provider.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<OrchestratorAgent>();
        var context = new AgentContext
        {
            OriginalPrompt = prompt
        };
        context.Properties["SdkPath"] = config.SdkPath;

        var result = await orchestrator.ExecuteAsync(context);

        Console.WriteLine();
        Console.WriteLine($"Resultado: {result.Status}");
        Console.WriteLine($"Mensaje: {result.Message}");

        if (context.GeneratedFiles.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Archivos generados ({context.GeneratedFiles.Count}):");
            foreach (var file in context.GeneratedFiles)
                Console.WriteLine($"  - {file.RelativePath} ({file.Type})");
        }

        if (context.ValidationResults.Count > 0)
        {
            var totalIssues = context.ValidationResults.Sum(v => v.Issues.Count);
            if (totalIssues > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"Validacion: {totalIssues} issues");
                foreach (var vr in context.ValidationResults)
                {
                    foreach (var issue in vr.Issues)
                        Console.WriteLine($"  [{issue.Severity}] {issue.FilePath}:{issue.Line} - {issue.Message}");
                }
            }
        }
    }
}
