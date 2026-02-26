using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Models.Sdk;
using EMIC_DevAgent.Core.Services.Llm;
using EMIC_DevAgent.Core.Services.Sdk;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents;

/// <summary>
/// Agente principal que recibe el prompt del usuario, clasifica el intent,
/// realiza desambiguacion LLM-driven con preguntas al usuario, y delega a subagentes.
/// </summary>
public class OrchestratorAgent : AgentBase
{
    private readonly ILlmService _llmService;
    private readonly IUserInteraction _userInteraction;
    private readonly IEnumerable<IAgent> _subAgents;
    private readonly ISdkScanner _sdkScanner;

    public OrchestratorAgent(
        ILlmService llmService,
        IUserInteraction userInteraction,
        IEnumerable<IAgent> subAgents,
        ISdkScanner sdkScanner,
        ILogger<OrchestratorAgent> logger)
        : base(logger)
    {
        _llmService = llmService;
        _userInteraction = userInteraction;
        _subAgents = subAgents;
        _sdkScanner = sdkScanner;
    }

    public override string Name => "Orchestrator";
    public override string Description => "Coordina el flujo completo: analiza intent, desambigua, delega a subagentes";

    public override bool CanHandle(AgentContext context) => true;

    protected override async Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        // 1. Interactive menu: identify abstraction layer + sub-type (hardcoded, no LLM)
        var menuHistory = await RunInitialMenuAsync(context, ct);

        // 2. Classify prompt to extract componentName, category, description (lightweight LLM)
        Logger.LogInformation("Classifying prompt for details: {Prompt}", Truncate(context.OriginalPrompt, 100));

        if (context.Analysis == null)
        {
            context.Analysis = await ClassifyIntentAsync(context.OriginalPrompt, ct);
            Logger.LogInformation("Prompt classified: Component={Component}, Category={Category}",
                context.Analysis.ComponentName, context.Analysis.Category);
        }

        // Override intent from menu (menu is authoritative, LLM only provides names)
        context.Analysis.Intent = context.Properties.TryGetValue("MenuIntent", out var mi)
            ? (IntentType)mi : context.Analysis.Intent;

        // 3. LLM-driven disambiguation (menu answers are pre-loaded in history)
        await _userInteraction.ReportProgressAsync(Name, "Iniciando desambiguacion...", 5, ct);
        var disambiguationResult = await RunDisambiguationAsync(context, menuHistory, ct);

        if (!disambiguationResult)
        {
            return AgentResult.Failure(Name, "Disambiguation failed — could not build specification");
        }

        // 3. Display specification
        await DisplaySpecificationAsync(context, ct);

        // 4. If DisambiguationOnly, stop here (no SDK scan, no generation)
        if (context.DisambiguationOnly)
        {
            Logger.LogInformation("DisambiguationOnly mode — stopping after specification");
            return AgentResult.Success(Name,
                $"Specification complete for {context.Specification?.ComponentName ?? "unknown"} ({context.Specification?.Intent})");
        }

        // 5. SDK scan AFTER disambiguation (to match spec against existing components)
        await _userInteraction.ReportProgressAsync(Name, "Scanning SDK inventory...", 15, ct);

        var sdkPath = context.Properties.TryGetValue("SdkPath", out var pathObj)
            ? pathObj.ToString() ?? string.Empty
            : string.Empty;

        try
        {
            context.SdkState = await _sdkScanner.ScanAsync(sdkPath, ct);
            Logger.LogInformation("SDK scan complete: {Apis} APIs, {Drivers} drivers, {Modules} modules",
                context.SdkState.Apis.Count, context.SdkState.Drivers.Count, context.SdkState.Modules.Count);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SDK scan failed, proceeding with empty inventory");
            context.SdkState = new SdkInventory();
        }

        // 6. Create generation plan (top-down design)
        if (context.Plan == null)
        {
            context.Plan = CreateGenerationPlan(context.Analysis);
            Logger.LogInformation("Generation plan created: {FileCount} files planned",
                context.Plan.FilesToGenerate.Count);
        }

        // 7. Execute sub-agents bottom-up
        await _userInteraction.ReportProgressAsync(Name, "Starting bottom-up execution...", 20, ct);

        var agentSequence = DetermineAgentSequence(context.Analysis.Intent);
        var totalSteps = agentSequence.Count;

        for (int i = 0; i < agentSequence.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var agentName = agentSequence[i];
            var agent = _subAgents.FirstOrDefault(a => a.Name.Equals(agentName, StringComparison.OrdinalIgnoreCase));

            if (agent == null)
            {
                Logger.LogWarning("Sub-agent '{AgentName}' not found, skipping", agentName);
                continue;
            }

            if (!agent.CanHandle(context))
            {
                Logger.LogDebug("Sub-agent '{AgentName}' cannot handle current context, skipping", agentName);
                continue;
            }

            var progress = 20 + (double)(i + 1) / totalSteps * 80;
            await _userInteraction.ReportProgressAsync(Name, $"Running {agentName}...", progress, ct);

            Logger.LogInformation("Executing sub-agent: {AgentName} ({Step}/{Total})", agentName, i + 1, totalSteps);
            var result = await agent.ExecuteAsync(context, ct);

            if (result.Status == ResultStatus.Failure)
            {
                Logger.LogError("Sub-agent '{AgentName}' failed: {Message}", agentName, result.Message);

                if (agentName == "Compilation")
                    return AgentResult.Failure(Name, $"Pipeline stopped: {agentName} failed — {result.Message}");

                if (agentName == "RuleValidator" && !result.Message.Contains("error", StringComparison.OrdinalIgnoreCase))
                    continue;

                return AgentResult.Failure(Name, $"Pipeline stopped: {agentName} failed — {result.Message}");
            }

            if (result.Status == ResultStatus.NeedsInput)
            {
                Logger.LogInformation("Sub-agent '{AgentName}' needs input", agentName);
                return AgentResult.NeedsInput(Name, result.Message);
            }
        }

        await _userInteraction.ReportProgressAsync(Name, "Pipeline completed successfully", 100, ct);

        var fileCount = context.GeneratedFiles.Count;
        var issueCount = context.ValidationResults.Sum(v => v.Issues.Count);
        return AgentResult.Success(Name,
            $"Pipeline completed: {fileCount} files generated" +
            (issueCount > 0 ? $", {issueCount} validation issues" : "") +
            (context.LastCompilation?.Success == true ? ", compilation successful" : ""));
    }

    #region Initial Menu (Hardcoded 2-Level Classification)

    private async Task<List<DisambiguationExchange>> RunInitialMenuAsync(AgentContext context, CancellationToken ct)
    {
        var history = new List<DisambiguationExchange>();

        // ── NIVEL 1: Capa de abstraccion ──
        var level1 = new DisambiguationQuestion
        {
            Question = "¿Que desea desarrollar?"
        };
        level1.Options.Add("Sistema distribuido — Red de dispositivos coordinados: maestro/esclavo, control descentralizado, monitoreo distribuido, redes de sensores/actuadores");
        level1.Options.Add("Dispositivo integrado — Equipo funcional autonomo: controlador, datalogger, monitor remoto, gateway, HMI, o combinacion de funciones");
        level1.Options.Add("Modulo EMIC — Nodo del ecosistema modular con funcion principal (sensor, actuador, display, teclado, comunicacion) + auxiliares. Incluye EMIC-Bus (I2C). Hardware reutilizable sin logica de negocio");
        level1.Options.Add("API EMIC — Capa de abstraccion de alto nivel: funciones, variables y eventos para la logica de negocio. Consume drivers y HAL internamente");
        level1.Options.Add("Driver EMIC — Control de hardware externo al microcontrolador: sensores, memorias, transceivers, displays. Usa HAL y expone funciones para la capa API");
        level1.Options.Add("Otro (especificar)");

        context.PendingQuestions.Add(level1);
        Logger.LogInformation("Menu Level 1: abstraction layer");
        var answer1 = await _userInteraction.AskQuestionAsync(level1, ct);
        level1.Answer = answer1;

        var exchange1 = new DisambiguationExchange
        {
            Question = level1.Question,
            Answer = answer1,
            Reason = "Identificar la capa de abstraccion a desarrollar"
        };
        foreach (var opt in level1.Options)
            exchange1.Options.Add(opt);
        history.Add(exchange1);

        Logger.LogInformation("Level 1 answer: {Answer}", Truncate(answer1, 100));

        // Determine which option was selected
        var level1Index = level1.Options.IndexOf(answer1);

        // ── NIVEL 2: Sub-tipo segun seleccion ──
        DisambiguationQuestion level2;
        switch (level1Index)
        {
            case 0: // Sistema distribuido
                context.Properties["MenuIntent"] = IntentType.CreateModule;
                context.Properties["MenuProjectType"] = ProjectType.DistributedSystem;

                level2 = new DisambiguationQuestion
                {
                    Question = "¿Que tipo de sistema distribuido?"
                };
                level2.Options.Add("Control de lazo cerrado — Sensores, controlador y actuadores en nodos separados con retroalimentacion en tiempo real");
                level2.Options.Add("Red de monitoreo — Nodos sensores con concentrador/gateway para recoleccion y visualizacion de datos");
                level2.Options.Add("Control maestro/esclavo — Nodo central que coordina y comanda nodos perifericos");
                level2.Options.Add("Sistema hibrido — Combinacion de control, monitoreo y comunicacion distribuida");
                level2.Options.Add("Otro (especificar)");
                break;

            case 1: // Dispositivo integrado
                context.Properties["MenuIntent"] = IntentType.CreateModule;
                context.Properties["MenuProjectType"] = ProjectType.Monolithic;

                level2 = new DisambiguationQuestion
                {
                    Question = "¿Cual es la funcion principal del dispositivo?"
                };
                level2.Options.Add("Controlador — Sistema de control con entradas de sensores y salidas a actuadores");
                level2.Options.Add("Datalogger — Adquisicion, procesamiento y almacenamiento de datos");
                level2.Options.Add("Monitor remoto — Sensado de variables con transmision de datos a sistema externo");
                level2.Options.Add("Gateway / Concentrador — Recibe datos de multiples fuentes, procesa y/o reenvia");
                level2.Options.Add("Interfaz HMI — Display, teclado y control local para operacion del usuario");
                level2.Options.Add("Dispositivo multi-funcion — Combinacion personalizada de funciones");
                level2.Options.Add("Otro (especificar)");
                break;

            case 2: // Modulo EMIC
                context.Properties["MenuIntent"] = IntentType.CreateModule;
                context.Properties["MenuProjectType"] = ProjectType.EmicModule;

                level2 = new DisambiguationQuestion
                {
                    Question = "¿Cual es la funcion principal del modulo?"
                };
                level2.Options.Add("Sensor — Medicion de magnitud fisica (temperatura, presion, humedad, etc.)");
                level2.Options.Add("Actuador — Control de dispositivo externo (motor, rele, valvula, calefactor, etc.)");
                level2.Options.Add("Display — Presentacion visual (LCD, OLED, 7 segmentos, LED matrix, etc.)");
                level2.Options.Add("Teclado / Entrada — Interfaz de usuario (botones, encoder rotativo, touch, etc.)");
                level2.Options.Add("Comunicacion — Bridge o gateway de protocolo (RS485, WiFi, Bluetooth, LoRa, etc.)");
                level2.Options.Add("Indicador — Señalizacion simple (LEDs, buzzer, alarma visual/sonora, etc.)");
                level2.Options.Add("Otro (especificar)");
                break;

            case 3: // API EMIC
                context.Properties["MenuIntent"] = IntentType.CreateApi;

                level2 = new DisambiguationQuestion
                {
                    Question = "¿Que tipo de API desea crear?"
                };
                level2.Options.Add("Procesamiento de señales — Filtros, conversiones, algoritmos, transformaciones de datos");
                level2.Options.Add("Protocolo de comunicacion — Abstraccion de protocolos de alto nivel (Modbus, MQTT, HTTP, etc.)");
                level2.Options.Add("Almacenamiento — Logging, gestion de EEPROM, sistema de archivos, buffers circulares");
                level2.Options.Add("Interfaz de usuario — Menus, pantallas, gestion de indicadores y entradas");
                level2.Options.Add("Control — PID, maquinas de estados, secuenciadores, automatismos");
                level2.Options.Add("Otro (especificar)");
                break;

            case 4: // Driver EMIC
                context.Properties["MenuIntent"] = IntentType.CreateDriver;

                level2 = new DisambiguationQuestion
                {
                    Question = "¿Que tipo de hardware externo controla el driver?"
                };
                level2.Options.Add("Sensor — Chip sensor (LM35, BME280, MPU6050, MAX6675, etc.)");
                level2.Options.Add("Conversor — ADC/DAC externo, expansor de I/O (MCP3008, PCF8574, etc.)");
                level2.Options.Add("Memoria — EEPROM, Flash SPI, tarjeta SD, FRAM, etc.");
                level2.Options.Add("Display — Controlador de display (HD44780, SSD1306, MAX7219, etc.)");
                level2.Options.Add("Transceptor / Comunicacion — Modulo de comunicacion (MCP2200, ESP8266, SX1278, HC-05, etc.)");
                level2.Options.Add("Motor / Actuador — Driver de potencia (L298, DRV8825, ULN2003, etc.)");
                level2.Options.Add("Otro (especificar)");
                break;

            default: // Otro
                context.Properties["MenuIntent"] = IntentType.Unknown;

                level2 = new DisambiguationQuestion
                {
                    Question = "Describa brevemente que desea crear:"
                };
                level2.Options.Add("Otro (especificar)");
                break;
        }

        context.PendingQuestions.Add(level2);
        Logger.LogInformation("Menu Level 2: sub-type for level1={Index}", level1Index);
        var answer2 = await _userInteraction.AskQuestionAsync(level2, ct);
        level2.Answer = answer2;

        var exchange2 = new DisambiguationExchange
        {
            Question = level2.Question,
            Answer = answer2,
            Reason = "Identificar el sub-tipo especifico"
        };
        foreach (var opt in level2.Options)
            exchange2.Options.Add(opt);
        history.Add(exchange2);

        Logger.LogInformation("Level 2 answer: {Answer}", Truncate(answer2, 100));

        // Map level 2 answer to enums
        var level2Index = level2.Options.IndexOf(answer2);
        MapLevel2ToEnums(context, level1Index, level2Index);

        return history;
    }

    private static void MapLevel2ToEnums(AgentContext context, int level1, int level2)
    {
        switch (level1)
        {
            case 0: // Sistema distribuido
                context.Properties["MenuSystemKind"] = level2 switch
                {
                    0 => SystemKind.ClosedLoopControl,
                    1 => SystemKind.Monitoring,
                    2 => SystemKind.RemoteControl,
                    3 => SystemKind.Combined,
                    _ => SystemKind.Other
                };
                break;

            case 1: // Dispositivo integrado
                context.Properties["MenuDeviceFunction"] = level2 switch
                {
                    0 => DeviceFunction.Controller,
                    1 => DeviceFunction.Datalogger,
                    2 => DeviceFunction.RemoteMonitor,
                    3 => DeviceFunction.Gateway,
                    4 => DeviceFunction.Hmi,
                    5 => DeviceFunction.MultiFunction,
                    _ => DeviceFunction.Other
                };
                break;

            case 2: // Modulo EMIC
                context.Properties["MenuModuleRole"] = level2 switch
                {
                    0 => ModuleRole.Sensor,
                    1 => ModuleRole.Actuator,
                    2 => ModuleRole.Display,
                    3 => ModuleRole.Input,
                    4 => ModuleRole.Communication,
                    5 => ModuleRole.Indicator,
                    _ => ModuleRole.Other
                };
                break;

            case 3: // API EMIC
                context.Properties["MenuApiType"] = level2 switch
                {
                    0 => ApiType.SignalProcessing,
                    1 => ApiType.CommunicationProtocol,
                    2 => ApiType.Storage,
                    3 => ApiType.UserInterface,
                    4 => ApiType.Control,
                    _ => ApiType.Other
                };
                break;

            case 4: // Driver EMIC
                context.Properties["MenuDriverTarget"] = level2 switch
                {
                    0 => DriverTarget.Sensor,
                    1 => DriverTarget.Converter,
                    2 => DriverTarget.Memory,
                    3 => DriverTarget.Display,
                    4 => DriverTarget.Transceiver,
                    5 => DriverTarget.MotorActuator,
                    _ => DriverTarget.Other
                };
                break;
        }
    }

    #endregion

    #region Intent Classification

    private async Task<PromptAnalysis> ClassifyIntentAsync(string prompt, CancellationToken ct)
    {
        var llmPrompt = new LlmPromptBuilder()
            .WithSystemInstruction(
                "You are an EMIC SDK intent classifier. Given a user prompt, extract:\n" +
                "intent: one of CreateModule, CreateApi, CreateDriver, ModifyExisting, QueryInfo, Unknown\n" +
                "componentName: name of the component (PascalCase, no spaces)\n" +
                "category: component category (e.g., Sensors, Communication, Actuators, LEDs, Display)\n" +
                "description: one-line description of what the user wants\n" +
                "dependencies: comma-separated list of required APIs/drivers (empty if none)\n" +
                "Respond ONLY with key=value lines, no explanations.")
            .WithUserPrompt(prompt)
            .Build();

        try
        {
            var response = await _llmService.GenerateWithContextAsync(llmPrompt.UserPrompt, llmPrompt.SystemPrompt, ct);
            return ParseIntentResponse(response);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "LLM intent classification failed, using defaults");
            return new PromptAnalysis
            {
                Intent = IntentType.Unknown,
                Description = prompt
            };
        }
    }

    private static PromptAnalysis ParseIntentResponse(string response)
    {
        var analysis = new PromptAnalysis();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = line.IndexOf('=');
            if (eqIdx <= 0) continue;
            values[line[..eqIdx].Trim()] = line[(eqIdx + 1)..].Trim();
        }

        if (values.TryGetValue("intent", out var intent))
        {
            analysis.Intent = intent switch
            {
                "CreateModule" => IntentType.CreateModule,
                "CreateApi" => IntentType.CreateApi,
                "CreateDriver" => IntentType.CreateDriver,
                "ModifyExisting" => IntentType.ModifyExisting,
                "QueryInfo" => IntentType.QueryInfo,
                _ => IntentType.Unknown
            };
        }

        if (values.TryGetValue("componentName", out var name))
            analysis.ComponentName = name;
        if (values.TryGetValue("category", out var cat))
            analysis.Category = cat;
        if (values.TryGetValue("description", out var desc))
            analysis.Description = desc;
        if (values.TryGetValue("dependencies", out var deps) && !string.IsNullOrWhiteSpace(deps))
        {
            foreach (var dep in deps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                analysis.RequiredDependencies.Add(dep);
        }

        return analysis;
    }

    #endregion

    #region LLM-Driven Disambiguation

    private async Task<bool> RunDisambiguationAsync(AgentContext context, List<DisambiguationExchange> menuHistory, CancellationToken ct)
    {
        const int maxIterations = 10;
        var conversationHistory = new List<DisambiguationExchange>(menuHistory);

        // --- LLM loop for technical details (menu already resolved layer + sub-type) ---
        for (int i = 0; i < maxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();

            Logger.LogInformation("Disambiguation iteration {Iteration}/{Max}", i + 1, maxIterations);

            var llmResponse = await GenerateLlmDisambiguationStepAsync(context, conversationHistory, ct);

            if (llmResponse == null)
            {
                Logger.LogWarning("LLM disambiguation returned null, forcing spec");
                await ForceFinalSpecificationAsync(context, conversationHistory, ct);
                return true;
            }

            // Parse LLM response: COMPLETE or QUESTION
            var parsed = ParseDisambiguationResponse(llmResponse);

            if (parsed.IsComplete)
            {
                context.Specification = parsed.Specification;
                context.Specification!.ConversationHistory.AddRange(conversationHistory);
                Logger.LogInformation("Disambiguation complete after {Iterations} iterations", i + 1);
                return true;
            }

            // It's a QUESTION — ask the user
            var question = new DisambiguationQuestion
            {
                Question = parsed.Question
            };
            foreach (var opt in parsed.Options)
                question.Options.Add(opt);

            context.PendingQuestions.Add(question);

            Logger.LogInformation("Asking disambiguation question: {Question}", parsed.Question);
            var answer = await _userInteraction.AskQuestionAsync(question, ct);
            question.Answer = answer;

            var exchange = new DisambiguationExchange
            {
                Question = parsed.Question,
                Answer = answer,
                Reason = parsed.Reason
            };
            foreach (var opt in parsed.Options)
                exchange.Options.Add(opt);
            conversationHistory.Add(exchange);

            Logger.LogInformation("User answered: {Answer}", Truncate(answer, 100));
        }

        // Reached max iterations — force best-effort spec
        Logger.LogWarning("Reached max disambiguation iterations ({Max}), forcing spec", maxIterations);
        await ForceFinalSpecificationAsync(context, conversationHistory, ct);
        return true;
    }

    private async Task<string?> GenerateLlmDisambiguationStepAsync(
        AgentContext context,
        List<DisambiguationExchange> history,
        CancellationToken ct)
    {
        var systemPrompt = GetDisambiguationSystemPrompt();
        var conversationSummary = BuildConversationSummary(history);

        var userPrompt =
            $"SOLICITUD DEL USUARIO: {context.OriginalPrompt}\n\n" +
            $"CLASIFICACION INICIAL:\n" +
            $"  intent={context.Analysis!.Intent}\n" +
            $"  componentName={context.Analysis.ComponentName}\n" +
            $"  category={context.Analysis.Category}\n" +
            $"  description={context.Analysis.Description}\n\n" +
            $"CONVERSACION HASTA AHORA:\n{conversationSummary}\n\n" +
            "Basandote en lo anterior, genera la siguiente QUESTION o emite COMPLETE con la especificacion JSON.";

        var llmPrompt = new LlmPromptBuilder()
            .WithSystemInstruction(systemPrompt)
            .WithUserPrompt(userPrompt)
            .Build();

        try
        {
            return await _llmService.GenerateWithContextAsync(llmPrompt.UserPrompt, llmPrompt.SystemPrompt, ct);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "LLM disambiguation step failed");
            return null;
        }
    }

    private static string GetDisambiguationSystemPrompt()
    {
        return
            "Eres un asistente de especificacion para el ecosistema EMIC. Tu objetivo es recopilar " +
            "los detalles tecnicos necesarios para producir una especificacion completa.\n\n" +

            "## CONTEXTO EMIC\n" +
            "EMIC es un framework modular para sistemas embebidos IoT/IIoT. Las capas de abstraccion son:\n" +
            "- **Sistema distribuido**: Red de dispositivos coordinados (maestro/esclavo, monitoreo, control descentralizado)\n" +
            "- **Dispositivo integrado**: Equipo standalone con multiples funciones (controlador, datalogger, monitor remoto, gateway, HMI)\n" +
            "- **Modulo EMIC**: Nodo modular con funcion principal + auxiliares. Incluye EMIC-Bus (I2C). Hardware reutilizable sin logica de negocio\n" +
            "- **API EMIC**: Capa de alto nivel con funciones, variables y eventos para logica de negocio. Consume drivers y HAL\n" +
            "- **Driver EMIC**: Capa de control de hardware externo. Usa HAL y expone funciones para la capa API\n\n" +

            "## IMPORTANTE: El usuario YA selecciono la capa de abstraccion y el sub-tipo via menu interactivo.\n" +
            "Esa informacion aparece en la CONVERSACION. NO re-preguntar tipo de proyecto ni rol del componente.\n" +
            "Ve directamente a los detalles tecnicos.\n\n" +

            "## FASES DE DESAMBIGUACION (solo las pendientes)\n\n" +

            "### Especificacion tecnica del componente\n" +
            "Segun el tipo ya seleccionado, preguntar detalles tecnicos especificos:\n" +
            "- Sensores: tipo de sensor, magnitud, rango, precision, interfaz electrica (I2C, SPI, analogico)\n" +
            "- Actuadores: tipo, potencia, interfaz de control\n" +
            "- Displays: tipo, protocolo, resolucion\n" +
            "- Indicadores: tipo (LED, buzzer), cantidad, patron\n" +
            "- Comunicacion: protocolo, velocidad, formato de trama\n" +
            "- APIs: funciones principales, eventos, variables\n" +
            "- Drivers: chip especifico, interfaz electrica, funciones a exponer\n\n" +

            "### Detalles de integracion\n" +
            "- PCB destino (si aplica)\n" +
            "- Microcontrolador preferido (si aplica)\n" +
            "- Funcionalidades auxiliares (timers, alarmas, logging, etc.)\n\n" +

            "## REGLAS CRITICAS\n" +
            "1. Cada pregunta debe tratar UN SOLO tema. NUNCA mezclar opciones de distinta naturaleza\n" +
            "2. Siempre incluir una opcion 'Otro (especificar)'\n" +
            "3. Si el tipo es 'Modulo EMIC', la comunicacion EMIC-Bus (I2C) es implicita. " +
            "NO preguntar por interfaz de comunicacion del modulo con el sistema\n" +
            "4. NO mencionar recursos del SDK ni componentes existentes\n" +
            "5. Hacer al menos 2-3 preguntas tecnicas antes de declarar COMPLETE\n" +
            "6. Responder en el mismo idioma que uso el usuario\n" +
            "7. NO repetir preguntas ya contestadas (incluyendo las del menu)\n" +
            "8. Si el usuario da informacion adicional en su respuesta, incorporarla sin re-preguntar\n\n" +

            "## FORMATOS DE RESPUESTA\n\n" +
            "FORMATO 1 — Cuando tienes suficiente informacion:\n" +
            "COMPLETE\n" +
            "{\n" +
            "  \"projectType\": \"Monolithic|EmicModule|DistributedSystem\",\n" +
            "  \"moduleRole\": \"Sensor|Actuator|Display|Input|Indicator|Communication|Other|Unknown\",\n" +
            "  \"systemKind\": \"ClosedLoopControl|Monitoring|RemoteControl|Combined|Other|Unknown\",\n" +
            "  \"deviceFunction\": \"Controller|Datalogger|RemoteMonitor|Gateway|Hmi|MultiFunction|Other|Unknown\",\n" +
            "  \"apiType\": \"SignalProcessing|CommunicationProtocol|Storage|UserInterface|Control|Other|Unknown\",\n" +
            "  \"driverTarget\": \"Sensor|Converter|Memory|Display|Transceiver|MotorActuator|Other|Unknown\",\n" +
            "  \"intent\": \"CreateModule|CreateApi|CreateDriver\",\n" +
            "  \"componentName\": \"NombrePascalCase\",\n" +
            "  \"category\": \"Sensors|Communication|Actuators|LEDs|Display|General\",\n" +
            "  \"description\": \"Descripcion en una linea\",\n" +
            "  \"sensorType\": \"tipo de sensor o vacio\",\n" +
            "  \"communicationInterface\": \"interfaz electrica del sensor (I2C/SPI/Analogico/etc) o vacio\",\n" +
            "  \"measurementRange\": \"rango o vacio\",\n" +
            "  \"measurementUnit\": \"unidad o vacio\",\n" +
            "  \"precision\": \"precision requerida o vacio\",\n" +
            "  \"targetPcb\": \"PCB destino o vacio\",\n" +
            "  \"chipOrProtocol\": \"chip o protocolo especifico o vacio\",\n" +
            "  \"outputType\": \"tipo de salida/comunicacion o vacio\",\n" +
            "  \"additionalDetails\": {\"clave\": \"valor\"}\n" +
            "}\n\n" +
            "FORMATO 2 — Cuando necesitas mas informacion:\n" +
            "QUESTION: Tu pregunta aqui\n" +
            "OPTIONS: Opcion1, Opcion2, Opcion3, Otro (especificar)\n" +
            "REASON: Por que necesitas esta informacion";
    }

    private static string BuildConversationSummary(List<DisambiguationExchange> history)
    {
        if (history.Count == 0)
            return "(ninguna pregunta realizada aun)";

        var lines = new List<string>();
        for (int i = 0; i < history.Count; i++)
        {
            var ex = history[i];
            lines.Add($"P{i + 1}: {ex.Question}");
            if (ex.Options.Count > 0)
                lines.Add($"  Opciones ofrecidas: {string.Join(", ", ex.Options)}");
            lines.Add($"  Respuesta del usuario: {ex.Answer}");
        }

        return string.Join("\n", lines);
    }

    private record DisambiguationParsed(
        bool IsComplete,
        DetailedSpecification? Specification,
        string Question,
        List<string> Options,
        string Reason);

    private DisambiguationParsed ParseDisambiguationResponse(string response)
    {
        var trimmed = response.Trim();

        // Check for COMPLETE format
        if (trimmed.StartsWith("COMPLETE", StringComparison.OrdinalIgnoreCase))
        {
            var jsonStart = trimmed.IndexOf('{');
            if (jsonStart >= 0)
            {
                var jsonEnd = FindMatchingBrace(trimmed, jsonStart);
                if (jsonEnd > jsonStart)
                {
                    var json = trimmed[jsonStart..(jsonEnd + 1)];
                    var spec = ParseSpecificationJson(json);
                    if (spec != null)
                    {
                        return new DisambiguationParsed(true, spec, "", new List<string>(), "");
                    }
                }
            }

            Logger.LogWarning("COMPLETE response but failed to parse JSON, treating as question");
        }

        // Parse QUESTION format
        var question = "";
        var options = new List<string>();
        var reason = "";

        foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var l = line.Trim();
            if (l.StartsWith("QUESTION:", StringComparison.OrdinalIgnoreCase))
                question = l["QUESTION:".Length..].Trim();
            else if (l.StartsWith("OPTIONS:", StringComparison.OrdinalIgnoreCase))
            {
                var optStr = l["OPTIONS:".Length..].Trim();
                foreach (var opt in SplitOptionsRespectingParentheses(optStr))
                    options.Add(opt);
            }
            else if (l.StartsWith("REASON:", StringComparison.OrdinalIgnoreCase))
                reason = l["REASON:".Length..].Trim();
        }

        // Fallback: if no QUESTION: prefix, use the whole response as the question
        if (string.IsNullOrEmpty(question))
            question = trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Podria dar mas detalles?";

        return new DisambiguationParsed(false, null, question, options, reason);
    }

    private DetailedSpecification? ParseSpecificationJson(string json)
    {
        try
        {
            var spec = new DetailedSpecification();
            var values = ParseJsonToDictionary(json);

            // Project type
            if (values.TryGetValue("projectType", out var pt))
            {
                spec.ProjectType = pt switch
                {
                    "Monolithic" => ProjectType.Monolithic,
                    "EmicModule" => ProjectType.EmicModule,
                    "DistributedSystem" => ProjectType.DistributedSystem,
                    _ => ProjectType.Unknown
                };
            }

            // Module role
            if (values.TryGetValue("moduleRole", out var mr2))
            {
                spec.ModuleRole = mr2 switch
                {
                    "Sensor" => ModuleRole.Sensor,
                    "Actuator" => ModuleRole.Actuator,
                    "Display" => ModuleRole.Display,
                    "Input" => ModuleRole.Input,
                    "Indicator" => ModuleRole.Indicator,
                    "Communication" => ModuleRole.Communication,
                    "Other" => ModuleRole.Other,
                    _ => ModuleRole.Unknown
                };
            }

            // System kind
            if (values.TryGetValue("systemKind", out var sk))
            {
                spec.SystemKind = sk switch
                {
                    "ClosedLoopControl" => SystemKind.ClosedLoopControl,
                    "Monitoring" => SystemKind.Monitoring,
                    "RemoteControl" => SystemKind.RemoteControl,
                    "Combined" => SystemKind.Combined,
                    "Other" => SystemKind.Other,
                    _ => SystemKind.Unknown
                };
            }

            // Device function
            if (values.TryGetValue("deviceFunction", out var df))
            {
                spec.DeviceFunction = df switch
                {
                    "Controller" => DeviceFunction.Controller,
                    "Datalogger" => DeviceFunction.Datalogger,
                    "RemoteMonitor" => DeviceFunction.RemoteMonitor,
                    "Gateway" => DeviceFunction.Gateway,
                    "Hmi" => DeviceFunction.Hmi,
                    "MultiFunction" => DeviceFunction.MultiFunction,
                    "Other" => DeviceFunction.Other,
                    _ => DeviceFunction.Unknown
                };
            }

            // API type
            if (values.TryGetValue("apiType", out var at))
            {
                spec.ApiType = at switch
                {
                    "SignalProcessing" => ApiType.SignalProcessing,
                    "CommunicationProtocol" => ApiType.CommunicationProtocol,
                    "Storage" => ApiType.Storage,
                    "UserInterface" => ApiType.UserInterface,
                    "Control" => ApiType.Control,
                    "Other" => ApiType.Other,
                    _ => ApiType.Unknown
                };
            }

            // Driver target
            if (values.TryGetValue("driverTarget", out var dt))
            {
                spec.DriverTarget = dt switch
                {
                    "Sensor" => DriverTarget.Sensor,
                    "Converter" => DriverTarget.Converter,
                    "Memory" => DriverTarget.Memory,
                    "Display" => DriverTarget.Display,
                    "Transceiver" => DriverTarget.Transceiver,
                    "MotorActuator" => DriverTarget.MotorActuator,
                    "Other" => DriverTarget.Other,
                    _ => DriverTarget.Unknown
                };
            }

            // Intent
            if (values.TryGetValue("intent", out var intent))
            {
                spec.Intent = intent switch
                {
                    "CreateModule" => IntentType.CreateModule,
                    "CreateApi" => IntentType.CreateApi,
                    "CreateDriver" => IntentType.CreateDriver,
                    "ModifyExisting" => IntentType.ModifyExisting,
                    "QueryInfo" => IntentType.QueryInfo,
                    _ => IntentType.Unknown
                };
            }

            if (values.TryGetValue("componentName", out var cn)) spec.ComponentName = cn;
            if (values.TryGetValue("category", out var cat)) spec.Category = cat;
            if (values.TryGetValue("description", out var desc)) spec.Description = desc;
            if (values.TryGetValue("sensorType", out var st)) spec.SensorType = st;
            if (values.TryGetValue("communicationInterface", out var ci)) spec.CommunicationInterface = ci;
            if (values.TryGetValue("measurementRange", out var mr)) spec.MeasurementRange = mr;
            if (values.TryGetValue("measurementUnit", out var mu)) spec.MeasurementUnit = mu;
            if (values.TryGetValue("precision", out var prec)) spec.Precision = prec;
            if (values.TryGetValue("targetPcb", out var tp)) spec.TargetPcb = tp;
            if (values.TryGetValue("chipOrProtocol", out var cp)) spec.ChipOrProtocol = cp;
            if (values.TryGetValue("outputType", out var ot)) spec.OutputType = ot;

            // Parse arrays (these stay for forward compatibility but won't be filled during disambiguation)
            foreach (var item in ParseJsonArray(json, "reusableApis"))
                spec.ReusableApis.Add(item);
            foreach (var item in ParseJsonArray(json, "reusableDrivers"))
                spec.ReusableDrivers.Add(item);
            foreach (var item in ParseJsonArray(json, "componentsToCreate"))
                spec.ComponentsToCreate.Add(item);
            foreach (var item in ParseJsonArray(json, "requiredDependencies"))
                spec.RequiredDependencies.Add(item);

            // Parse additionalDetails object
            var adIdx = json.IndexOf("\"additionalDetails\"", StringComparison.OrdinalIgnoreCase);
            if (adIdx >= 0)
            {
                var braceStart = json.IndexOf('{', adIdx);
                if (braceStart >= 0)
                {
                    var braceEnd = FindMatchingBrace(json, braceStart);
                    if (braceEnd > braceStart)
                    {
                        var innerJson = json[(braceStart + 1)..braceEnd];
                        foreach (var pair in ParseJsonToDictionary("{" + innerJson + "}"))
                            spec.AdditionalDetails[pair.Key] = pair.Value;
                    }
                }
            }

            return spec;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse specification JSON");
            return null;
        }
    }

    private static Dictionary<string, string> ParseJsonToDictionary(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;

        while (i < json.Length)
        {
            var keyStart = json.IndexOf('"', i);
            if (keyStart < 0) break;
            var keyEnd = json.IndexOf('"', keyStart + 1);
            if (keyEnd < 0) break;

            var key = json[(keyStart + 1)..keyEnd];
            i = keyEnd + 1;

            var colon = json.IndexOf(':', i);
            if (colon < 0) break;
            i = colon + 1;

            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length) break;

            if (json[i] == '"')
            {
                var valStart = i + 1;
                var valEnd = valStart;
                while (valEnd < json.Length)
                {
                    if (json[valEnd] == '\\') { valEnd += 2; continue; }
                    if (json[valEnd] == '"') break;
                    valEnd++;
                }
                result[key] = json[valStart..valEnd];
                i = valEnd + 1;
            }
            else if (json[i] == '[' || json[i] == '{')
            {
                var braceChar = json[i];
                var closeChar = braceChar == '[' ? ']' : '}';
                var depth = 1;
                i++;
                while (i < json.Length && depth > 0)
                {
                    if (json[i] == braceChar) depth++;
                    else if (json[i] == closeChar) depth--;
                    else if (json[i] == '"')
                    {
                        i++;
                        while (i < json.Length && json[i] != '"') { if (json[i] == '\\') i++; i++; }
                    }
                    i++;
                }
            }
            else
            {
                var valStart = i;
                while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']') i++;
                result[key] = json[valStart..i].Trim();
            }
        }

        return result;
    }

    private static List<string> ParseJsonArray(string json, string arrayName)
    {
        var result = new List<string>();
        var idx = json.IndexOf($"\"{arrayName}\"", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return result;

        var bracketStart = json.IndexOf('[', idx);
        if (bracketStart < 0) return result;

        var bracketEnd = json.IndexOf(']', bracketStart);
        if (bracketEnd < 0) return result;

        var inner = json[(bracketStart + 1)..bracketEnd];
        var i = 0;
        while (i < inner.Length)
        {
            var start = inner.IndexOf('"', i);
            if (start < 0) break;
            var end = start + 1;
            while (end < inner.Length)
            {
                if (inner[end] == '\\') { end += 2; continue; }
                if (inner[end] == '"') break;
                end++;
            }
            result.Add(inner[(start + 1)..end]);
            i = end + 1;
        }

        return result;
    }

    private async Task ForceFinalSpecificationAsync(
        AgentContext context,
        List<DisambiguationExchange> history,
        CancellationToken ct)
    {
        var systemPrompt =
            "Eres un asistente de especificacion EMIC. Se alcanzo el maximo de preguntas. " +
            "DEBES emitir COMPLETE con la mejor especificacion posible basada en la informacion disponible. " +
            "Usa el mismo formato JSON. Deja cadenas vacias para campos desconocidos.";

        var conversationSummary = BuildConversationSummary(history);

        var userPrompt =
            $"SOLICITUD: {context.OriginalPrompt}\n\n" +
            $"CLASIFICACION: intent={context.Analysis!.Intent}, component={context.Analysis.ComponentName}, " +
            $"category={context.Analysis.Category}\n\n" +
            $"CONVERSACION:\n{conversationSummary}\n\n" +
            "Emite COMPLETE con JSON ahora.";

        var llmPrompt = new LlmPromptBuilder()
            .WithSystemInstruction(systemPrompt)
            .WithUserPrompt(userPrompt)
            .Build();

        try
        {
            var response = await _llmService.GenerateWithContextAsync(llmPrompt.UserPrompt, llmPrompt.SystemPrompt, ct);
            var parsed = ParseDisambiguationResponse(response);

            if (parsed.IsComplete && parsed.Specification != null)
            {
                context.Specification = parsed.Specification;
                context.Specification.ConversationHistory.AddRange(history);
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Force-complete LLM call failed");
        }

        // Ultimate fallback: build spec from analysis
        context.Specification = new DetailedSpecification
        {
            Intent = context.Analysis.Intent,
            ComponentName = context.Analysis.ComponentName,
            Category = context.Analysis.Category,
            Description = context.Analysis.Description
        };
        context.Specification.ConversationHistory.AddRange(history);
        foreach (var dep in context.Analysis.RequiredDependencies)
            context.Specification.RequiredDependencies.Add(dep);
    }

    private async Task DisplaySpecificationAsync(AgentContext context, CancellationToken ct)
    {
        var spec = context.Specification;
        if (spec == null) return;

        // Apply menu overrides (menu is authoritative for these fields)
        if (context.Properties.TryGetValue("MenuProjectType", out var mpt))
            spec.ProjectType = (ProjectType)mpt;
        if (context.Properties.TryGetValue("MenuIntent", out var mint))
            spec.Intent = (IntentType)mint;
        if (context.Properties.TryGetValue("MenuModuleRole", out var mmr))
            spec.ModuleRole = (ModuleRole)mmr;
        if (context.Properties.TryGetValue("MenuSystemKind", out var msk))
            spec.SystemKind = (SystemKind)msk;
        if (context.Properties.TryGetValue("MenuDeviceFunction", out var mdf))
            spec.DeviceFunction = (DeviceFunction)mdf;
        if (context.Properties.TryGetValue("MenuApiType", out var mat))
            spec.ApiType = (ApiType)mat;
        if (context.Properties.TryGetValue("MenuDriverTarget", out var mdt))
            spec.DriverTarget = (DriverTarget)mdt;

        var lines = new List<string>
        {
            "",
            "=== ESPECIFICACION FINAL ==="
        };

        // Project architecture
        if (spec.ProjectType != ProjectType.Unknown)
        {
            var ptLabel = spec.ProjectType switch
            {
                ProjectType.Monolithic => "Dispositivo integrado (placa unica)",
                ProjectType.EmicModule => "Modulo EMIC (sistema modular, EMIC-Bus I2C)",
                ProjectType.DistributedSystem => "Sistema distribuido (multiples nodos)",
                _ => spec.ProjectType.ToString()
            };
            lines.Add($"  Tipo de proyecto: {ptLabel}");
        }

        if (spec.ModuleRole != ModuleRole.Unknown)
            lines.Add($"  Rol del modulo: {spec.ModuleRole}");

        if (spec.SystemKind != SystemKind.Unknown)
            lines.Add($"  Tipo de sistema: {spec.SystemKind}");

        if (spec.DeviceFunction != DeviceFunction.Unknown)
            lines.Add($"  Funcion del dispositivo: {spec.DeviceFunction}");

        if (spec.ApiType != ApiType.Unknown)
            lines.Add($"  Tipo de API: {spec.ApiType}");

        if (spec.DriverTarget != DriverTarget.Unknown)
            lines.Add($"  Hardware del driver: {spec.DriverTarget}");

        lines.Add($"  Componente: {spec.ComponentName}");
        lines.Add($"  Intent: {spec.Intent}");
        lines.Add($"  Categoria: {spec.Category}");
        lines.Add($"  Descripcion: {spec.Description}");

        if (!string.IsNullOrEmpty(spec.SensorType))
            lines.Add($"  Sensor: {spec.SensorType}");
        if (!string.IsNullOrEmpty(spec.CommunicationInterface))
            lines.Add($"  Interfaz del sensor: {spec.CommunicationInterface}");
        if (!string.IsNullOrEmpty(spec.MeasurementRange))
            lines.Add($"  Rango: {spec.MeasurementRange}");
        if (!string.IsNullOrEmpty(spec.MeasurementUnit))
            lines.Add($"  Unidad: {spec.MeasurementUnit}");
        if (!string.IsNullOrEmpty(spec.Precision))
            lines.Add($"  Precision: {spec.Precision}");
        if (!string.IsNullOrEmpty(spec.TargetPcb))
            lines.Add($"  PCB: {spec.TargetPcb}");
        if (!string.IsNullOrEmpty(spec.ChipOrProtocol))
            lines.Add($"  Chip/Protocolo: {spec.ChipOrProtocol}");
        if (!string.IsNullOrEmpty(spec.OutputType))
            lines.Add($"  Salida: {spec.OutputType}");

        if (spec.ReusableApis.Count > 0)
            lines.Add($"  APIs reutilizables: {string.Join(", ", spec.ReusableApis)}");
        if (spec.ReusableDrivers.Count > 0)
            lines.Add($"  Drivers reutilizables: {string.Join(", ", spec.ReusableDrivers)}");
        if (spec.ComponentsToCreate.Count > 0)
            lines.Add($"  Componentes a crear: {string.Join(", ", spec.ComponentsToCreate)}");
        if (spec.RequiredDependencies.Count > 0)
            lines.Add($"  Dependencias: {string.Join(", ", spec.RequiredDependencies)}");

        foreach (var kv in spec.AdditionalDetails)
            lines.Add($"  {kv.Key}: {kv.Value}");

        lines.Add($"  Preguntas realizadas: {spec.ConversationHistory.Count}");
        lines.Add("============================");
        lines.Add("");

        var text = string.Join("\n", lines);
        await _userInteraction.ReportProgressAsync(Name, text, null, ct);
    }

    private static int FindMatchingBrace(string text, int openIndex)
    {
        if (openIndex < 0 || openIndex >= text.Length) return -1;

        var openChar = text[openIndex];
        var closeChar = openChar == '{' ? '}' : openChar == '[' ? ']' : '\0';
        if (closeChar == '\0') return -1;

        var depth = 1;
        var i = openIndex + 1;
        var inString = false;

        while (i < text.Length && depth > 0)
        {
            var c = text[i];

            if (inString)
            {
                if (c == '\\') { i += 2; continue; }
                if (c == '"') inString = false;
            }
            else
            {
                if (c == '"') inString = true;
                else if (c == openChar) depth++;
                else if (c == closeChar) depth--;
            }

            if (depth > 0) i++;
        }

        return depth == 0 ? i : -1;
    }

    /// <summary>
    /// Splits an OPTIONS string by commas, but respects parentheses nesting.
    /// E.g. "Sensor digital (DS18B20, SHT30), Otro (especificar)" → 2 options, not 4.
    /// </summary>
    private static List<string> SplitOptionsRespectingParentheses(string input)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;

        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                var option = input[start..i].Trim();
                if (option.Length > 0)
                    result.Add(option);
                start = i + 1;
            }
        }

        // Last segment
        var last = input[start..].Trim();
        if (last.Length > 0)
            result.Add(last);

        return result;
    }

    #endregion

    #region Generation Plan & Agent Sequence

    private static GenerationPlan CreateGenerationPlan(PromptAnalysis analysis)
    {
        var plan = new GenerationPlan
        {
            ComponentName = analysis.ComponentName,
            ComponentType = analysis.Intent.ToString().Replace("Create", ""),
            Description = analysis.Description,
            RequiresProgramXml = analysis.Intent == IntentType.CreateModule
        };

        switch (analysis.Intent)
        {
            case IntentType.CreateApi:
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_api/{analysis.Category}/{analysis.ComponentName}/{analysis.ComponentName}.emic", Type = FileType.Emic, Purpose = "API definition" });
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_api/{analysis.Category}/{analysis.ComponentName}/inc/{analysis.ComponentName}.h", Type = FileType.Header, Purpose = "API header" });
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_api/{analysis.Category}/{analysis.ComponentName}/src/{analysis.ComponentName}.c", Type = FileType.Source, Purpose = "API source" });
                break;

            case IntentType.CreateDriver:
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_drivers/{analysis.Category}/{analysis.ComponentName}/{analysis.ComponentName}.emic", Type = FileType.Emic, Purpose = "Driver definition" });
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_drivers/{analysis.Category}/{analysis.ComponentName}/inc/{analysis.ComponentName}.h", Type = FileType.Header, Purpose = "Driver header" });
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_drivers/{analysis.Category}/{analysis.ComponentName}/src/{analysis.ComponentName}.c", Type = FileType.Source, Purpose = "Driver source" });
                break;

            case IntentType.CreateModule:
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_modules/{analysis.Category}/{analysis.ComponentName}/System/generate.emic", Type = FileType.Emic, Purpose = "Generation script" });
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_modules/{analysis.Category}/{analysis.ComponentName}/System/deploy.emic", Type = FileType.Emic, Purpose = "Deployment script" });
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_modules/{analysis.Category}/{analysis.ComponentName}/m_description.json", Type = FileType.Json, Purpose = "Module metadata" });
                break;
        }

        foreach (var dep in analysis.RequiredDependencies)
            plan.DependenciesToResolve.Add(dep);

        return plan;
    }

    private static List<string> DetermineAgentSequence(IntentType intent)
    {
        return intent switch
        {
            IntentType.CreateModule => new List<string>
            {
                "Analyzer",
                "DriverGenerator",
                "ApiGenerator",
                "ModuleGenerator",
                "ProgramXml",
                "RuleValidator",
                "Materializer",
                "Compilation"
            },
            IntentType.CreateApi => new List<string>
            {
                "Analyzer",
                "ApiGenerator",
                "RuleValidator",
                "Materializer",
                "Compilation"
            },
            IntentType.CreateDriver => new List<string>
            {
                "Analyzer",
                "DriverGenerator",
                "RuleValidator",
                "Materializer",
                "Compilation"
            },
            _ => new List<string>
            {
                "Analyzer",
                "RuleValidator"
            }
        };
    }

    #endregion

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
