using EMIC_DevAgent.Core.Agents;
using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Agents.Validators;
using EMIC_DevAgent.Core.Configuration;
using EMIC_DevAgent.Core.Services.Compilation;
using EMIC_DevAgent.Core.Services.Llm;
using EMIC_DevAgent.Core.Services.Metadata;
using EMIC_DevAgent.Core.Services.Sdk;
using EMIC_DevAgent.Core.Services.Templates;
using EMIC_DevAgent.Core.Services.Validation;
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
        ConfigureServices(services, config);
        var provider = services.BuildServiceProvider();

        var logger = provider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("EMIC DevAgent v0.1.0 - Agente de IA para desarrollo SDK EMIC");

        Console.WriteLine("EMIC DevAgent v0.1.0");
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

        var orchestrator = provider.GetRequiredService<OrchestratorAgent>();
        var context = new AgentContext { OriginalPrompt = prompt };

        try
        {
            var result = await orchestrator.ExecuteAsync(context);
            Console.WriteLine();
            Console.WriteLine($"Resultado: {result.Status}");
            Console.WriteLine($"Mensaje: {result.Message}");
        }
        catch (NotImplementedException)
        {
            Console.WriteLine();
            Console.WriteLine("Los agentes aun no estan implementados. Estructura creada correctamente.");
        }
    }

    private static void ConfigureServices(IServiceCollection services, EmicAgentConfig config)
    {
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        // Configuration
        services.AddSingleton(config);
        services.AddSingleton(SdkPaths.FromConfig(config));

        // Services
        services.AddSingleton<ILlmService, ClaudeLlmService>();
        services.AddSingleton<ISdkScanner, SdkScanner>();
        services.AddSingleton<SdkPathResolver>();
        services.AddSingleton<EmicFileParser>();
        services.AddSingleton<IMetadataService, MetadataService>();
        services.AddSingleton<ICompilationService>(sp =>
            throw new NotImplementedException("ICompilationService pendiente de implementacion"));
        services.AddSingleton<ITemplateEngine>(sp =>
            throw new NotImplementedException("ITemplateEngine pendiente de implementacion"));
        services.AddSingleton<CompilationErrorParser>();
        services.AddSingleton<ValidationService>();
        services.AddSingleton<LlmPromptBuilder>();

        // Templates
        services.AddSingleton<ApiTemplate>();
        services.AddSingleton<DriverTemplate>();
        services.AddSingleton<ModuleTemplate>();

        // Validators
        services.AddSingleton<IValidator, LayerSeparationValidator>();
        services.AddSingleton<IValidator, NonBlockingValidator>();
        services.AddSingleton<IValidator, StateMachineValidator>();
        services.AddSingleton<IValidator, DependencyValidator>();

        // Agents
        services.AddSingleton<AnalyzerAgent>();
        services.AddSingleton<ApiGeneratorAgent>();
        services.AddSingleton<DriverGeneratorAgent>();
        services.AddSingleton<ModuleGeneratorAgent>();
        services.AddSingleton<ProgramXmlAgent>();
        services.AddSingleton<CompilationAgent>();
        services.AddSingleton<RuleValidatorAgent>();
        services.AddSingleton<OrchestratorAgent>(sp =>
        {
            var subAgents = new List<IAgent>
            {
                sp.GetRequiredService<AnalyzerAgent>(),
                sp.GetRequiredService<ApiGeneratorAgent>(),
                sp.GetRequiredService<DriverGeneratorAgent>(),
                sp.GetRequiredService<ModuleGeneratorAgent>(),
                sp.GetRequiredService<ProgramXmlAgent>(),
                sp.GetRequiredService<CompilationAgent>(),
                sp.GetRequiredService<RuleValidatorAgent>()
            };
            return new OrchestratorAgent(
                sp.GetRequiredService<ILlmService>(),
                subAgents,
                sp.GetRequiredService<ILogger<OrchestratorAgent>>());
        });
    }
}
