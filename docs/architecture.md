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
| **OrchestratorAgent** | Recibe prompt, clasifica intent, desambigua con preguntas al usuario, delega a subagentes, coordina flujo completo |
| **AnalyzerAgent** | Escanea SDK (_api, _drivers, _modules, _hal), encuentra componentes reutilizables, identifica gaps |
| **ApiGeneratorAgent** | Genera archivos .emic, .h, .c para nuevas APIs siguiendo patrones existentes (led.emic, relay.emic) |
| **DriverGeneratorAgent** | Genera drivers para chips externos (.emic, .h, .c) usando HAL |
| **ModuleGeneratorAgent** | Genera generate.emic, deploy.emic, m_description.json para modulos completos |
| **ProgramXmlAgent** | Genera program.xml y archivos asociados para la logica de integracion |
| **CompilationAgent** | Compila con XC16, parsea errores, retropropaga correcciones |
| **RuleValidatorAgent** | Delega a 4 validadores especializados |

## Validadores Especializados

| Validador | Que verifica |
|-----------|-------------|
| **LayerSeparationValidator** | APIs no acceden registros directos (TRIS, LAT, PORT). Solo usan HAL_GPIO_*, HAL_SPI_*, etc. |
| **NonBlockingValidator** | No hay while() bloqueantes ni __delay_ms() en APIs. Usa getSystemMilis() + state machines |
| **StateMachineValidator** | Operaciones complejas usan patron switch(state) con variable estatica y timeouts |
| **DependencyValidator** | Todo EMIC:setInput referencia archivos existentes, no hay dependencias circulares |

## Estructura del Proyecto

```
EMIC_DevAgent/
    EMIC_DevAgent.sln
    docs/
        EMIC_Conceptos_Clave.md          # Conceptos clave del SDK EMIC
        architecture.md                   # Este archivo
    src/
        EMIC_DevAgent.Cli/               # Punto de entrada CLI
            Program.cs
            appsettings.json
        EMIC_DevAgent.Core/              # Logica principal
            Agents/                      # Agentes del sistema
                Base/                    # Interfaces y clases base
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
