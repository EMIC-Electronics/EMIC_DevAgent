using EMIC.Shared.Services.Storage;
using EMIC_DevAgent.Core.Agents;
using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Agents.Validators;
using EMIC_DevAgent.Core.Services.Compilation;
using EMIC_DevAgent.Core.Services.Llm;
using EMIC_DevAgent.Core.Services.Metadata;
using EMIC_DevAgent.Core.Services.Sdk;
using EMIC_DevAgent.Core.Services.Templates;
using EMIC_DevAgent.Core.Services.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EMIC_DevAgent.Core.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEmicDevAgent(this IServiceCollection services, EmicAgentConfig config)
    {
        // Configuration
        services.AddSingleton(config);
        services.AddSingleton(SdkPaths.FromConfig(config));

        // Validators (stateless)
        services.AddSingleton<IValidator, LayerSeparationValidator>();
        services.AddSingleton<IValidator, NonBlockingValidator>();
        services.AddSingleton<IValidator, StateMachineValidator>();
        services.AddSingleton<IValidator, DependencyValidator>();
        services.AddSingleton<IValidator, BackwardsCompatibilityValidator>();

        // Templates
        services.AddSingleton<ApiTemplate>();
        services.AddSingleton<DriverTemplate>();
        services.AddSingleton<ModuleTemplate>();
        services.AddSingleton<ITemplateEngine, TemplateEngineService>();

        // Singleton services (stateless)
        services.AddTransient<LlmPromptBuilder>();
        services.AddSingleton<CompilationErrorParser>();
        services.AddSingleton<SourceMapper>();

        // MediaAccess (scoped, needs IAgentSession for user context)
        services.AddScoped<MediaAccess>(sp =>
        {
            var session = sp.GetRequiredService<IAgentSession>();
            var drivers = new Dictionary<string, string>(session.VirtualDrivers);
            drivers["DEV"] = session.SdkPath;
            return new MediaAccess(session.UserEmail, drivers);
        });

        // Scoped services (per-request state)
        services.AddScoped<SdkPathResolver>();
        services.AddScoped<EmicFileParser>();
        services.AddScoped<ISdkScanner, SdkScanner>();
        services.AddScoped<IMetadataService, MetadataService>();
        services.AddScoped<ValidationService>();

        // Agents (scoped)
        services.AddScoped<AnalyzerAgent>();
        services.AddScoped<ApiGeneratorAgent>();
        services.AddScoped<DriverGeneratorAgent>();
        services.AddScoped<ModuleGeneratorAgent>();
        services.AddScoped<ProgramXmlAgent>();
        services.AddScoped<CompilationAgent>();
        services.AddScoped<RuleValidatorAgent>();
        services.AddScoped<MaterializerAgent>();

        // OrchestratorAgent with factory to resolve sub-agents
        services.AddScoped<OrchestratorAgent>(sp =>
        {
            var subAgents = new List<IAgent>
            {
                sp.GetRequiredService<AnalyzerAgent>(),
                sp.GetRequiredService<ApiGeneratorAgent>(),
                sp.GetRequiredService<DriverGeneratorAgent>(),
                sp.GetRequiredService<ModuleGeneratorAgent>(),
                sp.GetRequiredService<ProgramXmlAgent>(),
                sp.GetRequiredService<CompilationAgent>(),
                sp.GetRequiredService<RuleValidatorAgent>(),
                sp.GetRequiredService<MaterializerAgent>()
            };
            return new OrchestratorAgent(
                sp.GetRequiredService<ILlmService>(),
                sp.GetRequiredService<IUserInteraction>(),
                subAgents,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OrchestratorAgent>>());
        });

        // Fallback: NullAgentEventSink if host doesn't register one
        services.TryAddSingleton<IAgentEventSink, NullAgentEventSink>();

        // NOT registered here (host responsibility):
        // - IUserInteraction
        // - IAgentSession
        // - ILlmService
        // - ICompilationService
        // - ITemplateEngine

        return services;
    }
}
