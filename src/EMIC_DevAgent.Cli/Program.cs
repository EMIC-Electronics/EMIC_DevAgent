using EMIC_DevAgent.Core.Agents;
using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Configuration;
using EMIC_DevAgent.Core.Services.Compilation;
using EMIC_DevAgent.Core.Services.Llm;
using EMIC_DevAgent.Core.Services.Templates;
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

        // Core services
        services.AddEmicDevAgent(config);

        // CLI-specific: host-provided implementations
        services.AddSingleton<ILlmService, ClaudeLlmService>();
        services.AddSingleton<IUserInteraction, ConsoleUserInteraction>();
        services.AddSingleton<IAgentSession, CliAgentSession>();
        services.AddSingleton<IAgentEventSink, ConsoleEventSink>();

        // Stubs pendientes de implementacion
        services.AddSingleton<ICompilationService>(sp =>
            throw new NotImplementedException("ICompilationService pendiente de implementacion"));
        services.AddSingleton<ITemplateEngine>(sp =>
            throw new NotImplementedException("ITemplateEngine pendiente de implementacion"));

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

        using var scope = provider.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<OrchestratorAgent>();
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
}
