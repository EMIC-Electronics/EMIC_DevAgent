# EMIC DevAgent - Arquitectura de Agentes

## Diagrama de Flujo Principal

```
PROMPT USUARIO
    |
    v
[OrchestratorAgent]
    |-- Analiza intent (CreateModule/CreateApi/CreateDriver)
    |-- Loop de desambiguacion (preguntas al usuario)
    |
    v
[AnalyzerAgent]
    |-- Escanea SDK completo (_api, _drivers, _modules, _hal)
    |-- Encuentra APIs/drivers reutilizables
    |-- Identifica que falta crear
    |
    v
[Decision] -- Necesita API nueva? -----> [ApiGeneratorAgent]
           -- Necesita Driver nuevo? --> [DriverGeneratorAgent]
           -- Necesita Modulo? --------> [ModuleGeneratorAgent]
           -- Necesita program.xml? ---> [ProgramXmlAgent]
    |
    v
[RuleValidatorAgent]
    |-- LayerSeparation: PASS/FAIL
    |-- NonBlocking: PASS/FAIL
    |-- StateMachine: PASS/FAIL
    |-- Dependencies: PASS/FAIL
    |-- Si FAIL --> corregir y re-validar
    |
    v
[CompilationAgent]
    |-- Compila con XC16
    |-- Si errores --> parsea, corrige, re-compila (max 5 intentos)
    |
    v
[Metadata Update] --> .emic-meta.json en cada carpeta
    |
    v
REPORTE FINAL AL USUARIO
```

## Agentes y Responsabilidades

| Agente | Responsabilidad |
|--------|----------------|
| **OrchestratorAgent** | Recibe prompt, clasifica intent, desambigua via `IUserInteraction`, delega a subagentes, coordina flujo completo |
| **AnalyzerAgent** | Escanea SDK (_api, _drivers, _modules, _hal), encuentra componentes reutilizables, identifica gaps |
| **ApiGeneratorAgent** | Genera archivos .emic, .h, .c para nuevas APIs siguiendo patrones existentes (led.emic, relay.emic) |
| **DriverGeneratorAgent** | Genera drivers para chips externos (.emic, .h, .c) usando HAL |
| **ModuleGeneratorAgent** | Genera generate.emic, deploy.emic, m_description.json para modulos completos |
| **ProgramXmlAgent** | Genera program.xml y archivos asociados para la logica de integracion |
| **CompilationAgent** | Compila con XC16, parsea errores, resuelve ubicacion original via .map files (⚠️ SourceMapper con `// @source:` markers es obsoleto, pendiente migrar a .map TSV) |
| **RuleValidatorAgent** | Delega a 5 validadores especializados |

## Validadores Especializados

| Validador | Que verifica |
|-----------|-------------|
| **LayerSeparationValidator** | APIs no acceden registros directos (TRIS, LAT, PORT). Solo usan HAL_GPIO_*, HAL_SPI_*, etc. |
| **NonBlockingValidator** | No hay while() bloqueantes ni __delay_ms() en APIs. Usa getSystemMilis() + state machines |
| **StateMachineValidator** | Operaciones complejas usan patron switch(state) con variable estatica y timeouts |
| **DependencyValidator** | Todo EMIC:setInput referencia archivos existentes, no hay dependencias circulares |
| **BackwardsCompatibilityValidator** | Verifica EMIC:ifdef/#ifdef guards en .emic/.h/.c para funcionalidad nueva. Core functions (init, poll) se excluyen |

## Separacion Core/CLI

EMIC_DevAgent.Core esta disenado como **libreria pura** sin acoplamiento a ningun host especifico. Tiene dos consumidores previstos:
1. **EMIC_DevAgent.Cli** - Herramienta de linea de comandos (actual)
2. **EMIC.Web.IDE** - Agente embebido en la aplicacion web (futuro)

### Interfaces de abstraccion (en Core)

| Interfaz | Proposito | CLI implementa | Web implementara |
|----------|----------|----------------|-----------------|
| `IUserInteraction` | Preguntas, confirmaciones, progreso | `ConsoleUserInteraction` | `SignalRUserInteraction` |
| `IAgentSession` | Contexto usuario/SDK | `CliAgentSession` | `WebAgentSession` |
| `IAgentEventSink` | Eventos en tiempo real (steps, archivos, compilacion) | `ConsoleEventSink` | `SignalREventSink` |

### Registro DI

Core expone `AddEmicDevAgent(config)` extension method que registra todos los servicios internos. Cada host solo agrega sus implementaciones especificas:

```csharp
// CLI Program.cs
services.AddEmicDevAgent(config);
services.AddSingleton<IUserInteraction, ConsoleUserInteraction>();
services.AddSingleton<IAgentSession, CliAgentSession>();
services.AddSingleton<IAgentEventSink, ConsoleEventSink>();
services.AddSingleton<ILlmService, ClaudeLlmService>();
```

Si el host no registra `IAgentEventSink`, Core usa `NullAgentEventSink` como fallback.

### Lifetimes

| Servicio | Lifetime | Razon |
|----------|----------|-------|
| Config, SdkPaths, Validadores, Templates | Singleton | Stateless |
| LlmPromptBuilder, CompilationErrorParser | Singleton | Stateless |
| Agentes, SdkScanner, EmicFileParser, MetadataService | Scoped | Dependen de contexto por usuario/request |
| OrchestratorAgent | Scoped | Factory que resuelve sub-agents del scope |

### Diagrama de capas

```
+-------------------+     +---------------------------+
| EMIC_DevAgent.Cli |     | EMIC.Web.IDE              |
|   (Console host)  |     |   (ASP.NET host, futuro)  |
|                   |     |                           |
| ConsoleInteraction|     | SignalRInteraction        |
| CliAgentSession   |     | WebAgentSession           |
| ConsoleEventSink  |     | SignalREventSink          |
+--------+----------+     +------------+--------------+
         |                              |
         |   ProjectReference           |   ProjectReference
         v                              v
+--------------------------------------------------+
|              EMIC_DevAgent.Core                   |
|                                                  |
|  Interfaces:                                     |
|    IUserInteraction, IAgentSession,              |
|    IAgentEventSink, ILlmService,                 |
|    ISdkScanner, IValidator, ICompilationService  |
|                                                  |
|  Extension: AddEmicDevAgent()                    |
|  Agentes, Pipeline, Validadores, Templates       |
+--------------------------------------------------+
```

## Estructura del Proyecto

```
EMIC_DevAgent/
    EMIC_DevAgent.sln
    docs/
        EMIC_Conceptos_Clave.md          # Conceptos clave del SDK EMIC
        architecture.md                   # Este archivo
        ESTADO_ACTUAL.md                  # Estado actual del proyecto
        PENDIENTES.md                     # Tareas pendientes de implementacion
        WORKFLOW.md                       # Reglas de workflow del agente
        MEJORAS_Y_SERVICIOS_COMPARTIDOS.md  # Analisis de servicios compartidos (historico)
    src/
        EMIC_DevAgent.Cli/               # Punto de entrada CLI
            Program.cs                   # Entry point (usa AddEmicDevAgent + registros CLI)
            CliAgentSession.cs           # IAgentSession para CLI
            ConsoleUserInteraction.cs    # IUserInteraction para CLI
            ConsoleEventSink.cs          # IAgentEventSink para CLI
            appsettings.json
        EMIC_DevAgent.Core/              # Logica principal (libreria pura)
            Agents/                      # Agentes del sistema
                Base/                    # Interfaces y clases base
                    IAgent.cs
                    AgentBase.cs
                    AgentContext.cs       # Contexto compartido + enums
                    AgentMessage.cs
                    AgentResult.cs
                    IUserInteraction.cs  # Abstraccion de interaccion con usuario
                    IAgentEventSink.cs   # Abstraccion de eventos/progreso
                Validators/              # Validadores especializados
            Orchestration/               # Pipeline de orquestacion
            Models/                      # Modelos de datos
                Sdk/                     # Inventario del SDK
                Metadata/                # Metadata .emic-meta.json
                Generation/              # Archivos generados
            Services/                    # Servicios del sistema
                Llm/                     # Integracion con Claude
                Sdk/                     # Scanner y parser del SDK
                Templates/               # Templates de generacion
                Metadata/                # Gestion de metadata
                Compilation/             # Compilacion XC16
                Validation/              # Validacion de reglas
            Configuration/               # Configuracion
                EmicAgentConfig.cs
                SdkPaths.cs
                IAgentSession.cs         # Abstraccion de sesion usuario/SDK
                ServiceCollectionExtensions.cs  # AddEmicDevAgent() extension method
                NullAgentEventSink.cs    # Fallback no-op para IAgentEventSink
    tests/
        EMIC_DevAgent.Tests/             # Tests unitarios
```

## Contexto Compartido (AgentContext)

El `AgentContext` es el objeto que viaja entre todos los agentes y contiene:

- `OriginalPrompt` - Prompt del usuario
- `Analysis` - Clasificacion del intent (CreateModule/CreateApi/CreateDriver)
- `SdkState` - Inventario completo del SDK
- `Plan` - Plan de generacion con archivos a crear
- `GeneratedFiles` - Archivos generados por los agentes
- `PendingQuestions` - Preguntas de desambiguacion pendientes
- `ValidationResults` - Resultados de validacion de reglas
- `LastCompilation` - Ultimo resultado de compilacion

## Formato de Metadata (.emic-meta.json)

Cada carpeta del SDK contiene un archivo `.emic-meta.json` para tracking:

```json
{
  "$schema": "emic-metadata-v1",
  "component": {
    "type": "api|driver|module",
    "name": "NombreComponente",
    "path": "_api/Category/Name"
  },
  "relationships": {
    "dependsOn": [{"type": "hal", "path": "_hal/GPIO/gpio.emic"}],
    "usedBy": [{"type": "module", "path": "_modules/.../ModuleName"}],
    "provides": {
      "functions": ["func1", "func2"],
      "dictionaries": {"inits": "...", "polls": "...", "main_includes": "...", "c_modules": "..."}
    }
  },
  "quality": {
    "compilation": "pass|fail|not_attempted",
    "ruleValidation": {"layerSeparation": "pass", "nonBlocking": "pass"},
    "debugState": "stable|in_progress|broken"
  },
  "history": [{"date": "...", "action": "created", "agent": "ApiGeneratorAgent"}]
}
```
