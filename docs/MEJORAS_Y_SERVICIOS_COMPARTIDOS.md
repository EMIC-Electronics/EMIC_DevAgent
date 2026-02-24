# EMIC DevAgent - Mejoras, Cambios y Servicios Compartidos con EMIC.Shared

> Fecha: Febrero 2026 (actualizado post-Fase 0)
> Autor: Analisis automatizado Claude Code
> Objetivo: Identificar servicios de EMIC.Shared reutilizables y recomendar mejoras arquitectonicas

---

## Resumen Ejecutivo

El proyecto EMIC_DevAgent tiene **22 stubs que lanzan NotImplementedException**. Tras analizar EMIC.Shared, se identifico que **al menos 10 de esos stubs duplican funcionalidad que ya existe y esta probada en produccion** en EMIC.Shared. Reutilizar estos servicios eliminaria entre un 40-50% del trabajo pendiente y garantizaria compatibilidad con el ecosistema EMIC existente.

**Impacto estimado:**
- **10 stubs eliminados** por reutilizacion directa de EMIC.Shared
- **4 stubs simplificados** significativamente al consumir servicios existentes
- **8 stubs** que si requieren implementacion nueva (agentes y validadores)
- **Eliminacion de riesgo**: no hay divergencia entre como DevAgent y el IDE manejan paths/compilacion/discovery

---

## 0. Separacion Core/CLI: Preparar para Embedding en EMIC.Web.IDE ✅ COMPLETADO

### Contexto
`EMIC_DevAgent.Core` tiene **dos consumidores** previstos:
1. **EMIC_DevAgent.Cli** - Herramienta de linea de comandos (actual)
2. **EMIC.Web.IDE** - Agente embebido en la aplicacion web (futuro)

Core es una **libreria pura** sin acoplamiento a ningun host especifico.

### Lo que se implemento

- `IUserInteraction` en `Core/Agents/Base/` — preguntas, progreso, confirmacion
- `IAgentEventSink` en `Core/Agents/Base/` — eventos de steps, archivos, validacion, compilacion
- `IAgentSession` en `Core/Configuration/` — email, SdkPath, VirtualDrivers
- `NullAgentEventSink` en `Core/Configuration/` — fallback no-op (TryAdd)
- `ServiceCollectionExtensions.AddEmicDevAgent()` en `Core/Configuration/` — registro DI centralizado
- `ConsoleUserInteraction` en `Cli/` — implementacion CLI con Console
- `CliAgentSession` en `Cli/` — sesion fija "devagent@local"
- `ConsoleEventSink` en `Cli/` — logging de eventos por consola
- `OrchestratorAgent` modificado para recibir `IUserInteraction` como dependencia
- `Program.cs` refactorizado: usa `AddEmicDevAgent()` + registros CLI + `CreateScope()`
- Lifetimes corregidos: Scoped para agentes/servicios con estado, Singleton para stateless

### Diseno anterior (referencia):

### 0.1 Interfaz `IUserInteraction` para comunicacion bidireccional ✅ IMPLEMENTADO

El `OrchestratorAgent` necesita hacer preguntas al usuario y reportar progreso. Actualmente no hay mecanismo para esto en Core.

**Problema:** Sin una abstraccion, cuando se implemente el orchestrator tendra que:
- En CLI: usar `Console.ReadLine()` (acoplamiento directo)
- En Web: usar SignalR Hub (acoplamiento a ASP.NET)

**Solucion:** Crear interfaz en Core que ambos hosts implementen:

```csharp
// EMIC_DevAgent.Core/Agents/Base/IUserInteraction.cs
public interface IUserInteraction
{
    /// Hace una pregunta al usuario y espera respuesta
    Task<string> AskQuestionAsync(DisambiguationQuestion question, CancellationToken ct);

    /// Reporta progreso al usuario (no espera respuesta)
    Task ReportProgressAsync(string agentName, string message, double? progressPercent, CancellationToken ct);

    /// Pide confirmacion antes de una accion (escribir archivos, compilar, etc.)
    Task<bool> ConfirmActionAsync(string description, CancellationToken ct);
}
```

**Implementaciones por host:**

```csharp
// EMIC_DevAgent.Cli/ConsoleUserInteraction.cs
public class ConsoleUserInteraction : IUserInteraction
{
    public async Task<string> AskQuestionAsync(DisambiguationQuestion question, CancellationToken ct)
    {
        Console.WriteLine(question.Question);
        for (int i = 0; i < question.Options.Count; i++)
            Console.WriteLine($"  {i + 1}. {question.Options[i]}");
        Console.Write("> ");
        return Console.ReadLine() ?? "";
    }

    public Task ReportProgressAsync(string agentName, string message, double? percent, CancellationToken ct)
    {
        Console.WriteLine($"[{agentName}] {message}");
        return Task.CompletedTask;
    }

    public async Task<bool> ConfirmActionAsync(string description, CancellationToken ct)
    {
        Console.Write($"{description} (s/n) > ");
        return Console.ReadLine()?.ToLower() == "s";
    }
}
```

```csharp
// EMIC.Web.IDE (futuro)
public class SignalRUserInteraction : IUserInteraction
{
    private readonly IHubContext<DevAgentHub> _hub;
    private readonly string _connectionId;
    // Usa SignalR para push al browser y TaskCompletionSource para esperar respuesta
}
```

**Inyeccion:** `IUserInteraction` se inyecta en `OrchestratorAgent` (y cualquier agente que necesite interaccion). El host lo registra en DI.

### 0.2 `IAgentSession` para contexto de ejecucion por usuario ✅ IMPLEMENTADO

**Problema:** `MediaAccess` requiere un `userName` (email del usuario autenticado). En CLI es un valor fijo, en Web viene del claim OAuth. Actualmente no hay forma de que Core obtenga esta informacion sin acoplarse al host.

**Solucion:** Crear interfaz de sesion:

```csharp
// EMIC_DevAgent.Core/Configuration/IAgentSession.cs
public interface IAgentSession
{
    /// Email del usuario (para MediaAccess)
    string UserEmail { get; }

    /// Path al SDK sobre el que se trabaja
    string SdkPath { get; }

    /// Virtual drivers adicionales (TARGET, SYS, etc.)
    Dictionary<string, string> VirtualDrivers { get; }
}
```

**Implementaciones:**
```csharp
// CLI
public class CliAgentSession : IAgentSession
{
    public string UserEmail => "devagent@local";
    public string SdkPath { get; init; }  // de appsettings
    public Dictionary<string, string> VirtualDrivers { get; init; } = new();
}

// Web (futuro)
public class WebAgentSession : IAgentSession
{
    public string UserEmail { get; init; }  // del claim OAuth
    public string SdkPath { get; init; }    // del repo seleccionado
    public Dictionary<string, string> VirtualDrivers { get; init; }
}
```

Con esto, `MediaAccess` se puede construir asi en DI:
```csharp
services.AddScoped<MediaAccess>(sp =>
{
    var session = sp.GetRequiredService<IAgentSession>();
    return new MediaAccess(session.UserEmail, session.VirtualDrivers);
});
```

### 0.3 DI Registration: mover de Program.cs a extension method en Core ✅ IMPLEMENTADO

El extension method `AddEmicDevAgent()` registra todos los servicios Core con lifetimes correctos (Singleton para stateless, Scoped para con-estado). Incluye factory para OrchestratorAgent con resolucion de sub-agents y `TryAddSingleton<IAgentEventSink, NullAgentEventSink>` como fallback.

**Uso actual en CLI Program.cs:**
```csharp
services.AddEmicDevAgent(config);
services.AddSingleton<ILlmService, ClaudeLlmService>();
services.AddSingleton<IUserInteraction, ConsoleUserInteraction>();
services.AddSingleton<IAgentSession, CliAgentSession>();
services.AddSingleton<IAgentEventSink, ConsoleEventSink>();

// Resolucion con scope
using var scope = provider.CreateScope();
var orchestrator = scope.ServiceProvider.GetRequiredService<OrchestratorAgent>();
```

**Uso futuro en Web:**
```csharp
services.AddEmicDevAgent(config);
services.AddScoped<IUserInteraction, SignalRUserInteraction>();
services.AddScoped<IAgentSession>(sp => /* construir desde HttpContext.User */);
services.AddScoped<ILlmService>(sp => /* reusar ClaudeAiService del IDE */);
```

### 0.4 Lifetime de servicios: Singleton vs Scoped ✅ IMPLEMENTADO

Lifetimes configurados en `AddEmicDevAgent()`:

| Servicio | Lifetime | Razon |
|----------|----------|-------|
| Config, SdkPaths | Singleton | Inmutable |
| Validadores (IValidator) | Singleton | Stateless |
| Templates (Api, Driver, Module) | Singleton | Stateless |
| LlmPromptBuilder, CompilationErrorParser | Singleton | Stateless |
| ISdkScanner, SdkPathResolver, EmicFileParser | Scoped | Pueden tener estado por operacion |
| IMetadataService, ValidationService | Scoped | Dependen de contexto |
| Todos los Agentes | Scoped | Dependen de servicios scoped |
| OrchestratorAgent | Scoped (factory) | Resuelve sub-agents del scope |
| IAgentEventSink (NullAgentEventSink) | Singleton (TryAdd) | Fallback stateless |

En CLI, el scope se crea manualmente:
```csharp
using var scope = provider.CreateScope();
var orchestrator = scope.ServiceProvider.GetRequiredService<OrchestratorAgent>();
```

### 0.5 Eventos y callbacks de progreso (para UI web en tiempo real) ✅ IMPLEMENTADO

`IAgentEventSink` implementado en Core con `NullAgentEventSink` como fallback. CLI usa `ConsoleEventSink`.

**Interfaz implementada:**

```csharp
// EMIC_DevAgent.Core/Agents/Base/IAgentEventSink.cs
public interface IAgentEventSink
{
    Task OnStepStarted(string stepName, string agentName, CancellationToken ct);
    Task OnStepCompleted(string stepName, AgentResult result, CancellationToken ct);
    Task OnFileGenerated(GeneratedFile file, CancellationToken ct);
    Task OnValidationResult(ValidationResult result, CancellationToken ct);
    Task OnCompilationResult(CompilationResult result, CancellationToken ct);
}
```

- **CLI:** Implementacion simple que logea a consola
- **Web:** Implementacion que pushea via SignalR al browser

Esto permite que el `OrchestrationPipeline` emita eventos sin saber quien los consume.

### 0.6 Resumen de interfaces creadas en Core ✅ IMPLEMENTADO

| Interfaz | Proposito | CLI implementacion | Web (futuro) |
|----------|----------|-------------------|--------------|
| `IUserInteraction` | Preguntas, confirmaciones, progreso | `ConsoleUserInteraction` | `SignalRUserInteraction` |
| `IAgentSession` | Contexto usuario/SDK | `CliAgentSession` | `WebAgentSession` |
| `IAgentEventSink` | Eventos en tiempo real | `ConsoleEventSink` | `SignalREventSink` |
| `AddEmicDevAgent()` | Registro DI compartido | Consumido en Program.cs | Consumido por Startup/Program |
| `NullAgentEventSink` | Fallback no-op | Registrado via TryAdd | Reemplazado por SignalR impl |

### 0.7 Diagrama de capas con separacion ✅ IMPLEMENTADO

```
+-------------------+     +---------------------------+
| EMIC_DevAgent.Cli |     | EMIC.Web.IDE              |
|   (Console host)  |     |   (ASP.NET host)          |
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
|  Agentes, Pipeline, Validadores, Templates       |
|  (toda la logica de negocio)                     |
+----------------------------+---------------------+
                             |
                             |   ProjectReference
                             v
+--------------------------------------------------+
|              EMIC.Shared                          |
|                                                  |
|  MediaAccess, TreeMaker, DiscoveryService,       |
|  ClaudeAiService, CompilerService, Repositories  |
+--------------------------------------------------+
```

---

## 1. Cambio Arquitectonico Principal: Agregar Referencia a EMIC.Shared

### Estado actual
EMIC_DevAgent.Core solo depende de paquetes Microsoft.Extensions.* (Abstractions). No tiene referencia a EMIC.Shared, por lo que reimplementa desde cero funcionalidad que ya existe.

### Cambio propuesto
Agregar `ProjectReference` a EMIC.Shared en `EMIC_DevAgent.Core.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\EMIC.Shared\EMIC.Shared.csproj" />
</ItemGroup>
```

### Consideraciones
- EMIC.Shared usa `net8.0` (compatible)
- EMIC.Shared trae dependencias transitivas (Npgsql, MailKit, HtmlAgilityPack, etc.) que DevAgent no necesita
- **Alternativa**: crear un proyecto `EMIC.Shared.Core` con solo los servicios necesarios (MediaAccess, TreeMaker, DiscoveryService, ClaudeAiService, CompilerService, modelos) y sin dependencias de DB/Web. Esto es mas limpio pero requiere mas trabajo de refactoring.
- **Recomendacion pragmatica**: empezar con referencia directa a EMIC.Shared; extraer EMIC.Shared.Core mas adelante si las dependencias transitivas causan problemas.

---

## 2. Mapeo de Stubs a Servicios Existentes en EMIC.Shared

### 2.1 SdkPathResolver.ResolveVolume() --> MediaAccess.EmicPath()

| Aspecto | DevAgent (stub) | EMIC.Shared (implementado) |
|---------|-----------------|---------------------------|
| **Archivo** | `Services/Sdk/SdkPathResolver.cs` | `Services/Storage/MediaAccess.cs` |
| **Metodo** | `ResolveVolume(string emicPath)` | `EmicPath(string path)` |
| **Funcion** | Resolver `DEV:`, `TARGET:`, `SYS:`, `USER:` | Resuelve todos los volumenes virtuales |
| **Estado** | `throw NotImplementedException` | En produccion, probado |

**Accion:** Eliminar `SdkPathResolver.ResolveVolume()` y usar `MediaAccess.EmicPath()` directamente. Mantener los metodos helper (`GetApiPath()`, `GetDriversPath()`, etc.) como wrappers.

**Ejemplo de migracion:**
```csharp
// ANTES (stub)
public string ResolveVolume(string emicPath) => throw new NotImplementedException();

// DESPUES
private readonly MediaAccess _mediaAccess;
public string ResolveVolume(string emicPath) => _mediaAccess.EmicPath(emicPath);
```

---

### 2.2 EmicFileParser --> TreeMaker

| Aspecto | DevAgent (stub) | EMIC.Shared (implementado) |
|---------|-----------------|---------------------------|
| **Archivo** | `Services/Sdk/EmicFileParser.cs` | `Services/Emic/TreeMaker.cs` |
| **Metodos** | `ParseApiEmicAsync()`, `ParseDriverEmicAsync()`, `ExtractDependenciesAsync()` | `Generate(string file)` |
| **Funcion** | Parsear archivos .emic | Parsea .emic completos, resuelve macros, genera codigo |
| **Estado** | Todo `NotImplementedException` | En produccion |

**Accion:** El `EmicFileParser` no necesita reimplementar el parsing de .emic. TreeMaker ya lo hace y expone los resultados via propiedades publicas:
- `emicDrivers` -> recursos descubiertos (funciones, eventos, variables)
- `sourcesFiles` -> archivos generados
- `misMacros` / `misMacros3` -> macros definidas
- `exceptions` -> errores encontrados

**Recomendacion:** Convertir `EmicFileParser` en un **adapter** sobre `TreeMaker`:
```csharp
public class EmicFileParser
{
    private readonly MediaAccess _mediaAccess;

    public async Task<ApiDefinition> ParseApiEmicAsync(string filePath, CancellationToken ct)
    {
        var treeMaker = new TreeMaker("devagent", _mediaAccess, new());
        treeMaker.modo = "Discovery";
        treeMaker.Generate(filePath);

        return MapToApiDefinition(treeMaker);
    }

    public async Task<List<string>> ExtractDependenciesAsync(string filePath, CancellationToken ct)
    {
        // TreeMaker ya resuelve dependencias via EMIC:setInput
        // Extraer de treeMaker.emicDrivers o del output
    }
}
```

**Nota importante:** TreeMaker es una clase con estado (no es stateless). Se instancia por operacion, no como singleton. Ajustar el registro DI en `Program.cs` para que sea `Transient` o usar factory.

---

### 2.3 SdkScanner --> DiscoveryService + Repositories

| Aspecto | DevAgent (stub) | EMIC.Shared (implementado) |
|---------|-----------------|---------------------------|
| **Archivo** | `Services/Sdk/SdkScanner.cs` | `Services/Emic/DiscoveryService.cs` + `Repositories.cs` |
| **Metodos** | `ScanAsync()`, `FindApiAsync()`, `FindDriverAsync()`, `FindModuleAsync()` | `RunFullDiscovery()`, `GetModulesList()`, `GetModuleCategories()` |
| **Funcion** | Escanear SDK completo | Escanea modulos, extrae recursos publicados |
| **Estado** | Todo `NotImplementedException` | En produccion |

**Accion:** `SdkScanner` debe delegar a los servicios existentes:

```csharp
public class SdkScanner : ISdkScanner
{
    private readonly MediaAccess _mediaAccess;
    private SdkInventory? _cachedInventory;

    public async Task<SdkInventory> ScanAsync(string sdkPath, CancellationToken ct)
    {
        if (_cachedInventory != null) return _cachedInventory;

        var inventory = new SdkInventory { SdkRootPath = sdkPath };

        // Usar Repositories (static) para enumerar modulos
        var modules = Repositories.GetModulesList("devagent", sdkPath);
        var categories = Repositories.GetModuleCategories("devagent", sdkPath);

        // Usar DiscoveryService para analisis profundo
        var discovery = new DiscoveryService("devagent", _mediaAccess);
        var report = discovery.RunFullDiscovery(sdkPath);

        // Mapear resultados a SdkInventory...
        _cachedInventory = inventory;
        return inventory;
    }
}
```

**Beneficios:**
- Reutiliza el scanner probado en produccion (usado por el IDE web)
- Descubre recursos reales (funciones con tags EMIC-Codify, no solo estructura de carpetas)
- Soporte para selectores/configuradores que el stub no contempla

---

### 2.4 ClaudeLlmService --> ClaudeAiService

| Aspecto | DevAgent (stub) | EMIC.Shared (implementado) |
|---------|-----------------|---------------------------|
| **Archivo** | `Services/Llm/ClaudeLlmService.cs` | `Services/ClaudeAiService.cs` |
| **Metodos** | `GenerateAsync()`, `GenerateWithContextAsync()`, `GenerateStructuredAsync<T>()` | `GenerateResponseAsync(prompt, context, userType)` |
| **Funcion** | Llamar a Claude API | Llama a Claude API con system prompt EMIC |
| **Estado** | Todo `NotImplementedException` | En produccion |

**Accion:** Hay dos opciones:

**Opcion A (Rapida):** Usar `ClaudeAiService` directamente como implementacion de `ILlmService`:
```csharp
public class ClaudeLlmService : ILlmService
{
    private readonly IClaudeAiService _claudeService;

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct)
        => await _claudeService.GenerateResponseAsync(prompt);

    public async Task<string> GenerateWithContextAsync(string prompt, string context, CancellationToken ct)
        => await _claudeService.GenerateResponseAsync(prompt, context, "expert");
}
```

**Opcion B (Recomendada):** Extender `ClaudeAiService` o crear un wrapper que:
- Soporte `GenerateStructuredAsync<T>()` (pedir JSON + deserializar)
- Use modelos mas potentes (Sonnet/Opus) para generacion de codigo vs Haiku para chat
- Tenga system prompts especializados para cada agente (no el generico del asistente)
- Implemente retry con backoff exponencial

**Recomendacion:** Opcion B. El `ClaudeAiService` actual usa Haiku y tiene un system prompt orientado al asistente del IDE, no a la generacion de codigo SDK. Pero su infraestructura HTTP (manejo de API key, headers, parsing de respuesta) se debe reutilizar.

---

### 2.5 ICompilationService + CompilationErrorParser --> CompilerService / BuildOrchestrator

| Aspecto | DevAgent (stub) | EMIC.Shared (implementado) |
|---------|-----------------|---------------------------|
| **Archivo** | `Services/Compilation/` | `Services/Emic/CompilerService.cs` + `Services/Build/` |
| **Metodos** | `CompileAsync()`, `Parse()` | `Compile()`, `ParseCompilerOutput()`, `CompilationResultToXml()` |
| **Funcion** | Compilar con XC16 y parsear errores | Compila con XC8/XC16/XC32, parsea output |
| **Estado** | Todo `NotImplementedException` | En produccion (CompilerService deprecated, Build/ activo) |

**Accion:** No reimplementar la invocacion de compiladores. EMIC.Shared ya tiene:
- Deteccion de instalacion de XC compilers (Windows/Linux/macOS)
- Construccion de linea de comandos por MCU family
- Parsing de errores/warnings con ubicacion en archivo fuente
- `CompilationResult` con memoria usada, paths de salida, etc.

```csharp
// Implementar ICompilationService usando CompilerService de EMIC.Shared
public class Xc16CompilationService : ICompilationService
{
    private readonly MediaAccess _mediaAccess;

    public async Task<CompilationResult> CompileAsync(string projectPath, CancellationToken ct)
    {
        var compiler = new CompilerService(_mediaAccess, projectPath);
        var result = compiler.Compile(cModules);
        return MapToAgentCompilationResult(result);
    }
}
```

**Nota:** `CompilerService` esta marcado `[Obsolete]`. Investigar si `BuildOrchestrator` en `Services/Build/` es mejor opcion para nuevas implementaciones.

---

### 2.6 MetadataService --> MediaAccess.ReadJsonAsync<T> / WriteJsonAsync<T>

| Aspecto | DevAgent (stub) | EMIC.Shared (implementado) |
|---------|-----------------|---------------------------|
| **Archivo** | `Services/Metadata/MetadataService.cs` | `Services/Storage/MediaAccess.cs` |
| **Metodos** | `ReadMetadataAsync()`, `WriteMetadataAsync()`, `UpdateHistoryAsync()` | `ReadJsonAsync<T>()`, `WriteJsonAsync<T>()` |

**Accion:** `MediaAccess` ya tiene metodos genericos para leer/escribir JSON con virtual paths. `MetadataService` solo necesita usarlos:

```csharp
public class MetadataService : IMetadataService
{
    private readonly MediaAccess _mediaAccess;

    public async Task<FolderMetadata?> ReadMetadataAsync(string folderPath, CancellationToken ct)
    {
        var metaPath = Path.Combine(folderPath, MetadataFileName);
        if (!_mediaAccess.File.Exists(metaPath)) return null;
        return await _mediaAccess.ReadJsonAsync<FolderMetadata>(metaPath);
    }

    public async Task WriteMetadataAsync(string folderPath, FolderMetadata metadata, CancellationToken ct)
    {
        var metaPath = Path.Combine(folderPath, MetadataFileName);
        await _mediaAccess.WriteJsonAsync(metaPath, metadata);
    }
}
```

---

### 2.7 ProgramXmlAgent --> EmicXmlToCConverter (ElementEMICGenerator)

| Aspecto | DevAgent (stub) | EMIC.Shared (implementado) |
|---------|-----------------|---------------------------|
| **Archivo** | `Agents/ProgramXmlAgent.cs` | `Services/Emic/ElementEMICGenerator.cs` |
| **Funcion** | Generar program.xml | Convierte XML de editor visual a codigo C |

**Accion:** La conversion XML<->C ya existe. `ProgramXmlAgent` debe:
- Usar LLM para generar el **contenido** del program.xml (logica del integrador)
- Usar `ElementEMICGenerator` para **validar** que el XML generado produce C compilable
- No reimplementar el parser XML→C

---

## 3. Tabla Resumen: Stubs vs Servicios Compartidos

| # | Stub en DevAgent | Servicio en EMIC.Shared | Accion |
|---|-----------------|------------------------|--------|
| 1 | `SdkPathResolver.ResolveVolume()` | `MediaAccess.EmicPath()` | **Delegar** |
| 2 | `EmicFileParser.ParseApiEmicAsync()` | `TreeMaker.Generate()` | **Adapter** |
| 3 | `EmicFileParser.ParseDriverEmicAsync()` | `TreeMaker.Generate()` | **Adapter** |
| 4 | `EmicFileParser.ExtractDependenciesAsync()` | `TreeMaker` (dependencias internas) | **Adapter** |
| 5 | `SdkScanner.ScanAsync()` | `DiscoveryService.RunFullDiscovery()` | **Delegar** |
| 6 | `SdkScanner.FindApiAsync()` | `Repositories.GetModulesList()` | **Delegar** |
| 7 | `SdkScanner.FindDriverAsync()` | `Repositories.GetModulesList()` | **Delegar** |
| 8 | `SdkScanner.FindModuleAsync()` | `Repositories.getModule()` | **Delegar** |
| 9 | `ClaudeLlmService.GenerateAsync()` | `ClaudeAiService.GenerateResponseAsync()` | **Extender** |
| 10 | `ClaudeLlmService.GenerateWithContextAsync()` | `ClaudeAiService.GenerateResponseAsync()` | **Extender** |
| 11 | `ClaudeLlmService.GenerateStructuredAsync<T>()` | No existe directo | **Implementar nuevo** sobre infra existente |
| 12 | `LlmPromptBuilder.Build()` | No existe directo | **Implementar nuevo** (reutilizar system prompt) |
| 13 | `ICompilationService` | `CompilerService.Compile()` | **Wrapper** |
| 14 | `CompilationErrorParser.Parse()` | `CompilerService.ParseCompilerOutput()` | **Delegar** |
| 15 | `MetadataService.ReadMetadataAsync()` | `MediaAccess.ReadJsonAsync<T>()` | **Delegar** |
| 16 | `MetadataService.WriteMetadataAsync()` | `MediaAccess.WriteJsonAsync<T>()` | **Delegar** |
| 17 | `MetadataService.UpdateHistoryAsync()` | `MediaAccess.ReadJsonAsync/WriteJsonAsync` | **Componer** |
| 18 | `ApiTemplate.GenerateAsync()` | No existe | **Implementar nuevo** |
| 19 | `DriverTemplate.GenerateAsync()` | No existe | **Implementar nuevo** |
| 20 | `ModuleTemplate.GenerateAsync()` | No existe | **Implementar nuevo** |
| 21 | `ValidationService.ValidateAllAsync()` | No existe | **Implementar nuevo** |
| 22 | `OrchestrationPipeline.ExecuteAsync()` | No existe | **Implementar nuevo** |

**Resultado:** 17 de 22 stubs se resuelven total o parcialmente con EMIC.Shared. Solo 5 requieren implementacion completamente nueva.

---

## 4. Mejoras Arquitectonicas Recomendadas

### 4.1 Inyeccion de MediaAccess como servicio central

**Problema:** DevAgent crea su propia abstraccion de paths (`SdkPathResolver`) que es redundante con `MediaAccess`.

**Solucion:** Registrar `MediaAccess` en DI como servicio singleton e inyectarlo en todos los servicios que necesiten acceso a archivos:

```csharp
// Program.cs
services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<EmicAgentConfig>();
    var virtualDrivers = new Dictionary<string, string>
    {
        ["DEV"] = config.SdkPath,
        ["TARGET"] = Path.Combine(config.SdkPath, "_target")
    };
    return new MediaAccess("devagent", virtualDrivers);
});
```

### 4.2 TreeMaker como Transient (no Singleton)

**Problema:** `TreeMaker` tiene estado mutable (`emicDrivers`, `sourcesFiles`, `misMacros`). No es safe como singleton.

**Solucion:** Registrar como factory:
```csharp
services.AddTransient(sp =>
{
    var ma = sp.GetRequiredService<MediaAccess>();
    return new TreeMaker("devagent", ma, new Dictionary<string, Dictionary<string, string>>());
});
```

O mejor, crear un `ITreeMakerFactory`:
```csharp
public interface ITreeMakerFactory
{
    TreeMaker Create(Dictionary<string, Dictionary<string, string>> macros = null);
}
```

### 4.3 Modelo de datos: unificar con modelos de EMIC.Shared

**Problema:** DevAgent define sus propios modelos (`ApiDefinition`, `DriverDefinition`, `ModuleDefinition`, `SdkInventory`) que duplican conceptos de EMIC.Shared (`ModuleInfo`, `DiscoveredResources`, `DiscoveryReport`, `EmicDriver`).

**Solucion:** Dos opciones:
1. **Mapping layer**: Mantener modelos propios de DevAgent pero crear mappers desde/hacia modelos de EMIC.Shared
2. **Usar modelos de EMIC.Shared directamente**: Reemplazar los POCOs propios por los de EMIC.Shared donde sea equivalente

**Recomendacion:** Opcion 1 (mapping). Los modelos de DevAgent tienen campos especificos para el agente (como `GeneratedByAgent`, `ValidationResults`) que no existen en EMIC.Shared. Mantenerlos separados pero con conversion automatica.

### 4.4 Separar configuracion de LLM del ClaudeAiService existente

**Problema:** `ClaudeAiService` en EMIC.Shared esta hardcodeado a Haiku con un system prompt orientado al asistente del IDE. DevAgent necesita Sonnet/Opus con system prompts especializados por agente.

**Solucion:** Crear `DevAgentLlmService` que reutilice la infraestructura HTTP de `ClaudeAiService` pero con:
- Modelo configurable (Sonnet 4.5 por defecto para generacion de codigo)
- System prompts por agente (OrchestratorAgent, ApiGeneratorAgent, etc.)
- Soporte para respuestas estructuradas (JSON mode)
- Temperature ajustable por tipo de tarea (0.1 para clasificacion, 0.3 para generacion)

### 4.5 Agregar ITemplateEngine basado en archivos del SDK real

**Problema:** Los templates (`ApiTemplate`, `DriverTemplate`, `ModuleTemplate`) estan como stubs sin contenido. El plan sugiere parametrizar archivos reales del SDK como base.

**Solucion:** Usar `MediaAccess` para leer archivos de referencia reales del SDK (`_api/Indicators/LEDs/`) y generar templates a partir de ellos:

```csharp
public class ApiTemplate
{
    private readonly MediaAccess _mediaAccess;

    public async Task<List<GeneratedFile>> GenerateAsync(...)
    {
        // Leer led.emic como template de referencia
        var templateContent = _mediaAccess.File.ReadAllText("DEV:/_api/Indicators/LEDs/led.emic");
        // Parametrizar con variables del componente nuevo
        var generated = ApplyVariables(templateContent, variables);
        // ...
    }
}
```

---

## 5. Mejoras al Plan de Fases (PROXIMOS_PASOS.md)

### Fase 1 revisada: ya no es la mas critica

Con EMIC.Shared, la Fase 1 (EmicFileParser, SdkPathResolver, SdkScanner, MetadataService) se reduce drasticamente:

| Tarea original | Estimacion sin EMIC.Shared | Estimacion con EMIC.Shared |
|---------------|---------------------------|---------------------------|
| EmicFileParser | Implementacion completa | Adapter sobre TreeMaker (wrapper) |
| SdkPathResolver | Implementacion completa | 1 linea: delegar a MediaAccess.EmicPath() |
| SdkScanner | Implementacion completa | Delegar a DiscoveryService + Repositories |
| MetadataService | Implementacion completa | Delegar a MediaAccess.ReadJsonAsync/WriteJsonAsync |

### Nuevo orden de fases sugerido

```
FASE 0 (Separacion Core/CLI) ✅ COMPLETADO
  0.1 Interfaces de abstraccion (IUserInteraction, IAgentEventSink, IAgentSession)
  0.2 NullAgentEventSink (fallback)
  0.3 ServiceCollectionExtensions.AddEmicDevAgent()
  0.4 OrchestratorAgent recibe IUserInteraction
  0.5 Implementaciones CLI (ConsoleUserInteraction, CliAgentSession, ConsoleEventSink)
  0.6 Refactorizacion de Program.cs (scoped + extension method)

FASE 0.5 (Integracion EMIC.Shared - Infraestructura)
  0.5.1 Agregar referencia a EMIC.Shared
  0.5.2 Registrar MediaAccess en DI
  0.5.3 Adaptar EmicFileParser como wrapper de TreeMaker
  0.5.4 Implementar SdkScanner delegando a DiscoveryService
  0.5.5 Implementar MetadataService con MediaAccess JSON helpers
  0.5.6 Implementar SdkPathResolver.ResolveVolume() con MediaAccess
  0.5.7 Tests de integracion con SDK real

FASE 1 (Validadores - sin cambios)
  1.1-1.4 Validadores (implementacion nueva, no hay equivalente en EMIC.Shared)
  1.5 ValidationService

FASE 2 (LLM - parcialmente cubierta)
  2.1 Crear DevAgentLlmService reutilizando infraestructura de ClaudeAiService
  2.2 LlmPromptBuilder.Build() (reutilizar system prompt de ClaudeAiService como base)
  2.3 Implementar GenerateStructuredAsync<T>() (nuevo)

FASE 3 (Templates - implementacion nueva)
  3.1-3.3 Templates (usar archivos reales del SDK como base via MediaAccess)

FASE 4 (Agentes - implementacion nueva)
  4.1-4.7 Agentes (la logica de orquestacion es nueva)

FASE 5 (Compilacion - mayormente cubierta)
  5.1 Wrapper sobre CompilerService/BuildOrchestrator de EMIC.Shared
  5.2 CompilationErrorParser delegando a CompilerService.ParseCompilerOutput()

FASE 6 (Orquestacion + Testing)
  6.1 OrchestrationPipeline.ExecuteAsync()
  6.2 Tests integrales
```

---

## 6. Problemas Potenciales y Mitigaciones

### 6.1 Dependencias transitivas de EMIC.Shared
**Problema:** EMIC.Shared trae Npgsql, MailKit, HtmlAgilityPack que DevAgent no necesita.
**Mitigacion:** A corto plazo es aceptable (solo afecta tamano de deploy). A largo plazo, extraer EMIC.Shared.Core.

### 6.2 MediaAccess requiere userName
**Problema:** `MediaAccess` se construye con un `userName` (email). DevAgent no tiene concepto de usuario autenticado.
**Mitigacion:** Usar un userName fijo como `"devagent@system"` o `"local"`. Los paths que no dependen de USER: (como DEV:, SYSTEM:) no se ven afectados.

### 6.3 TreeMaker depende de contexto de compilacion
**Problema:** `TreeMaker.Generate()` espera un contexto de compilacion completo (macros, modo, virtual drivers).
**Mitigacion:** Usar `modo = "Discovery"` que es el modo ligero (no genera codigo, solo extrae recursos). Es exactamente lo que necesita `EmicFileParser`.

### 6.4 DiscoveryService usa TreeMaker internamente
**Problema:** `DiscoveryService.RunFullDiscovery()` instancia TreeMaker para cada modulo.
**Mitigacion:** Esto es correcto y esperado. TreeMaker como Transient, DiscoveryService maneja su ciclo de vida internamente.

### 6.5 Configuracion de API key de Claude
**Problema:** `ClaudeAiService` lee la API key de `IConfiguration` (appsettings). DevAgent tiene su propio `EmicAgentConfig`.
**Mitigacion:** Pasar la misma `IConfiguration` que usa DevAgent, o crear un wrapper que lea de `EmicAgentConfig.Llm.ApiKey`.

### 6.6 Diferencia de serializacion JSON
**Problema:** EMIC.Shared usa `Newtonsoft.Json` en algunos servicios y `System.Text.Json` en otros. DevAgent usa solo `System.Text.Json`.
**Mitigacion:** Para modelos nuevos (FolderMetadata, etc.) usar System.Text.Json. Los modelos de EMIC.Shared que usan Newtonsoft se consumen tal cual.

---

## 7. Servicios que DevAgent NO debe compartir (implementacion propia)

Estos componentes son especificos de DevAgent y no tienen equivalente en EMIC.Shared:

| Componente | Razon |
|-----------|-------|
| **4 Validadores** (Layer, NonBlocking, StateMachine, Dependency) | Logica de validacion especifica para codigo generado por IA |
| **ValidationService** | Orquestacion de validadores propios |
| **Templates** (Api, Driver, Module) | Generacion parametrizada especifica para el flujo del agente |
| **Todos los Agents** (8 agentes) | Logica de orquestacion IA es el core de DevAgent |
| **OrchestrationPipeline** | Pipeline de ejecucion especifico |
| **LlmPromptBuilder** | Construccion de prompts especializados por agente |
| **Modelos propios** (AgentContext, AgentResult, GenerationPlan, etc.) | Modelos de dominio del agente |

---

## 8. Otras Mejoras Recomendadas

### 8.1 Registro DI: eliminar factories que lanzan excepciones
**Actual:**
```csharp
services.AddSingleton<ICompilationService>(sp =>
    throw new NotImplementedException("ICompilationService pendiente"));
```
**Problema:** Falla al resolver el servicio, no al usarlo. Puede crashear la app incluso si el servicio no se usa en una sesion.
**Solucion:** Registrar implementaciones reales (wrappers sobre EMIC.Shared) o usar `Lazy<T>` para diferir la resolucion.

### 8.2 Agregar appsettings.json al repositorio
Actualmente falta el archivo de configuracion. Crear `appsettings.json` con valores por defecto y un `appsettings.Development.json` en .gitignore para secrets.

### 8.3 Tests: priorizar tests de integracion con SDK real
Con EMIC.Shared disponible, los tests mas valiosos son los de integracion que verifican:
- Escaneo real del SDK (via DiscoveryService)
- Parsing real de archivos .emic (via TreeMaker)
- Compilacion real con XC16 (via CompilerService)

### 8.4 Logging: unificar con EMIC.Shared
EMIC.Shared usa `Microsoft.Extensions.Logging`. DevAgent tambien. Asegurar que el LogLevel se configura de forma coherente y que los logs de TreeMaker/DiscoveryService se integran en el output de DevAgent.

### 8.5 Considerar modo "dry-run" para agentes
Agregar un modo donde los agentes generan archivos pero no los escriben al filesystem. Util para testing y para mostrar preview al usuario antes de confirmar.

### 8.6 Cache de SdkInventory
El escaneo del SDK es costoso (DiscoveryService recorre todo _modules/). Implementar cache:
- Serializar `SdkInventory` a JSON despues del primer scan
- Invalidar cache si cambia la fecha de modificacion del directorio SDK
- `SdkScanner` verifica cache antes de escanear

### 8.7 Alineacion de version de paquetes
DevAgent usa Microsoft.Extensions 8.0.x, EMIC.Shared usa 9.0.x. Unificar a la version mas reciente para evitar conflictos de binding redirect.

---

## 9. Diagrama de Dependencias Propuesto

```
EMIC_DevAgent.Cli
    |
    +--> EMIC_DevAgent.Core
              |
              +--> EMIC.Shared (ProjectReference)
              |       |
              |       +--> MediaAccess          (paths, file I/O)
              |       +--> TreeMaker            (parsing .emic)
              |       +--> DiscoveryService     (SDK scanning)
              |       +--> ClaudeAiService      (LLM infra)
              |       +--> CompilerService      (XC compilation)
              |       +--> Repositories         (module enumeration)
              |       +--> ConditionalProcessor (ifdef evaluation)
              |       +--> EmicException        (error reporting)
              |       +--> Models (ModuleInfo, DiscoveredResources, etc.)
              |
              +--> Agentes (OrchestratorAgent, AnalyzerAgent, etc.)
              +--> Validadores (Layer, NonBlocking, StateMachine, Dependency)
              +--> Templates (Api, Driver, Module)
              +--> Pipeline (OrchestrationPipeline)
              +--> Modelos propios (AgentContext, GenerationPlan, etc.)
```

---

## 10. Checklist de Implementacion

### Fase 0: Separacion Core/CLI ✅ COMPLETADO
- [x] Crear `IUserInteraction` en Core/Agents/Base/
- [x] Crear `IAgentSession` en Core/Configuration/
- [x] Crear `IAgentEventSink` en Core/Agents/Base/
- [x] Crear `NullAgentEventSink` en Core/Configuration/ (fallback)
- [x] Crear `ServiceCollectionExtensions.AddEmicDevAgent()` en Core/Configuration/
- [x] Mover registro DI de Program.cs al extension method
- [x] Crear `ConsoleUserInteraction` en Cli/
- [x] Crear `CliAgentSession` en Cli/
- [x] Crear `ConsoleEventSink` en Cli/
- [x] Cambiar lifetimes de Singleton a Scoped donde aplique
- [x] Actualizar Program.cs del CLI para usar scope + extension method
- [x] Agregar `IUserInteraction` como dependencia de `OrchestratorAgent`

### Fase 0.5: Integracion con EMIC.Shared
- [ ] Agregar `<ProjectReference>` a EMIC.Shared en EMIC_DevAgent.Core.csproj
- [ ] Registrar `MediaAccess` en DI (via IAgentSession)
- [ ] Implementar `SdkPathResolver.ResolveVolume()` delegando a `MediaAccess.EmicPath()`
- [ ] Convertir `EmicFileParser` en adapter sobre `TreeMaker`
- [ ] Implementar `SdkScanner` delegando a `DiscoveryService` + `Repositories`
- [ ] Implementar `MetadataService` usando `MediaAccess.ReadJsonAsync/WriteJsonAsync`
- [ ] Crear `DevAgentLlmService` reutilizando infra de `ClaudeAiService`
- [ ] Implementar `LlmPromptBuilder.Build()` con system prompts por agente
- [ ] Implementar `GenerateStructuredAsync<T>()` (JSON mode)
- [ ] Crear wrapper de `CompilerService` para `ICompilationService`
- [ ] Delegar `CompilationErrorParser.Parse()` a `CompilerService.ParseCompilerOutput()`
- [ ] Implementar 4 validadores (logica nueva)
- [ ] Implementar `ValidationService.ValidateAllAsync()`
- [ ] Implementar templates usando archivos reales del SDK via `MediaAccess`
- [ ] Implementar agentes (logica nueva)
- [ ] Implementar `OrchestrationPipeline.ExecuteAsync()`
- [ ] Agregar `appsettings.json` con configuracion por defecto
- [ ] Unificar versiones de paquetes NuGet
- [ ] Tests de integracion con SDK real
- [ ] Crear `appsettings.Development.json` (en .gitignore) para API key

---

*Este documento debe revisarse cada vez que se implemente una fase para actualizar el estado de las recomendaciones.*
