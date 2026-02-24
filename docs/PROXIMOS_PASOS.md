# EMIC DevAgent - Proximos Pasos y Estrategia de Desarrollo

---

## Estrategia General

El desarrollo sigue un enfoque **bottom-up**: implementar primero los servicios de bajo nivel (parsers, scanners) y luego los agentes que los consumen. Cada fase agrega una capa funcional completa que se puede testear independientemente.

### Principio rector
> Cada fase debe terminar con `dotnet build` sin errores y `dotnet test` con todos los tests pasando.

---

## Fase 1: Servicios de Infraestructura (Fundamentos)

**Objetivo:** Poder leer y entender el SDK EMIC desde C#.

### 1.1 EmicFileParser - Parser de archivos .emic
**Archivo:** `src/EMIC_DevAgent.Core/Services/Sdk/EmicFileParser.cs`
**Prioridad:** CRITICA (todo depende de esto)

Implementar parsing de archivos .emic para extraer:
- Lineas `EMIC:setInput DEV:/path/to/file.emic` → dependencias
- Lineas `EMIC:copy DEV:/source TARGET:/dest` → archivos a generar
- Lineas `EMIC:define dictName value` → entradas de diccionario
- Lineas `EMIC:ifdef usedFunction name` / `EMIC:endif` → condicionales
- Declaraciones de funciones API (las lineas con formato especial del .emic)

**Referencia:** Leer `_api/Indicators/LEDs/led.emic` y `_drivers/ADC/ADS1231/ADS1231.emic` como ejemplos.

**Tests a crear:**
- Parsear led.emic y verificar dependencias extraidas (gpio.emic, systemTimer.emic)
- Parsear led.emic y verificar archivos copy (led.h, led.c)
- Parsear led.emic y verificar diccionarios (inits, polls, main_includes, c_modules)
- Parsear ADS1231.emic y verificar que extrae dependencias de driver

### 1.2 SdkPathResolver.ResolveVolume()
**Archivo:** `src/EMIC_DevAgent.Core/Services/Sdk/SdkPathResolver.cs`

Implementar resolucion de volumenes logicos EMIC:
- `DEV:` → ruta del SDK root (ej: `C:\...\PIC_XC16`)
- `TARGET:` → ruta del directorio de generacion (configurable)
- `SYS:` → ruta del sistema EMIC
- `USER:` → ruta del usuario

**Tests:** Verificar que cada volumen se resuelve a un path absoluto correcto.

### 1.3 SdkScanner - Escaneo del SDK
**Archivo:** `src/EMIC_DevAgent.Core/Services/Sdk/SdkScanner.cs`

Implementar escaneo recursivo del filesystem:
- `ScanAsync()`: Recorrer `_api/`, `_drivers/`, `_modules/`, `_hal/` y construir `SdkInventory`
- `FindApiAsync()`: Buscar por nombre en la lista de APIs escaneadas
- `FindDriverAsync()`: Buscar por nombre en drivers
- `FindModuleAsync()`: Buscar por nombre en modulos

**Logica:** Para cada directorio que contenga un archivo `.emic`, usar `EmicFileParser` para extraer metadata y crear el `ApiDefinition`/`DriverDefinition`/`ModuleDefinition` correspondiente.

**Tests:**
- Escanear el SDK real y verificar que encuentra LEDs, Relay, ADS1231, HRD_LoRaWan
- FindApiAsync("LEDs") retorna ApiDefinition con funciones _state y _blink
- FindDriverAsync("ADS1231") retorna DriverDefinition con dependencia GPIO

### 1.4 MetadataService - Lectura/escritura de .emic-meta.json
**Archivo:** `src/EMIC_DevAgent.Core/Services/Metadata/MetadataService.cs`

Implementar con `System.Text.Json`:
- `ReadMetadataAsync()`: Leer y deserializar .emic-meta.json
- `WriteMetadataAsync()`: Serializar y escribir .emic-meta.json (pretty print)
- `UpdateHistoryAsync()`: Leer, agregar entrada de historial, escribir

**Tests:** Round-trip: escribir metadata, leer, verificar igualdad.

---

## Fase 2: Validadores (Reglas EMIC)

**Objetivo:** Poder validar codigo C generado contra las reglas del SDK.

### 2.1 LayerSeparationValidator
**Archivo:** `src/EMIC_DevAgent.Core/Agents/Validators/LayerSeparationValidator.cs`

Implementar con analisis de texto (regex):
- Buscar en archivos .c de APIs patrones prohibidos: `TRISAbits`, `LATAbits`, `PORTAbits`, `TRIS[A-Z]`, `LAT[A-Z]`, `PORT[A-Z]`
- Buscar inclusiones directas de headers de hardware
- Verificar que solo se usan funciones HAL_*

**Patron de busqueda:**
```
Prohibido en _api/:   TRISAbits, LATAbits, PORTAbits, TRIS[A-Z], LAT[A-Z], PORT[A-Z]
Permitido en _api/:   HAL_GPIO_*, HAL_SPI_*, HAL_I2C_*, HAL_UART_*, HAL_ADC_*, HAL_PWM_*
```

### 2.2 NonBlockingValidator
**Archivo:** `src/EMIC_DevAgent.Core/Agents/Validators/NonBlockingValidator.cs`

Buscar patrones bloqueantes:
- `while\s*\(` seguido de condicion que no sea `1` (loops de espera)
- `__delay_ms`, `__delay_us`, `delay(`, `_delay(`
- `for\s*\(.*;\s*;\s*\)` (for infinitos que no sean el main loop)

Excepcion: el `while(1)` en main.c es valido.

### 2.3 StateMachineValidator
**Archivo:** `src/EMIC_DevAgent.Core/Agents/Validators/StateMachineValidator.cs`

Verificar que funciones con operaciones multi-paso usan state machines:
- Buscar funciones con multiples operaciones secuenciales
- Verificar presencia de `static` + `switch` + `case` + patron de estado
- Warning si hay operaciones complejas sin state machine

### 2.4 DependencyValidator
**Archivo:** `src/EMIC_DevAgent.Core/Agents/Validators/DependencyValidator.cs`

- Para cada `EMIC:setInput`, verificar que el archivo referenciado existe
- Construir grafo de dependencias y detectar ciclos
- Verificar que no faltan dependencias transitivas

**Tests para todos los validadores:**
- Codigo valido del SDK real (led.c, relay.c) debe pasar todas las validaciones
- Codigo con `TRISAbits` inyectado debe fallar LayerSeparation
- Codigo con `__delay_ms()` inyectado debe fallar NonBlocking
- Archivo .emic con referencia a archivo inexistente debe fallar Dependency

### 2.5 ValidationService
**Archivo:** `src/EMIC_DevAgent.Core/Services/Validation/ValidationService.cs`

Implementar iteracion sobre todos los `IValidator` registrados:
```csharp
public async Task<List<ValidationResult>> ValidateAllAsync(AgentContext context, CancellationToken ct)
{
    var results = new List<ValidationResult>();
    foreach (var validator in _validators)
    {
        var result = await validator.ValidateAsync(context, ct);
        results.Add(result);
        _logger.LogInformation("Validator {Name}: {Status}", validator.Name, result.Passed ? "PASS" : "FAIL");
    }
    return results;
}
```

---

## Fase 3: Integracion con Claude API (LLM)

**Objetivo:** Poder hacer llamadas a Claude para clasificar intents y generar codigo.

### 3.1 Agregar Anthropic SDK
Agregar paquete NuGet `Anthropic.SDK` al proyecto Core:
```xml
<PackageReference Include="Anthropic.SDK" Version="..." />
```

Alternativamente, usar `HttpClient` directo con la API REST de Anthropic.

### 3.2 ClaudeLlmService
**Archivo:** `src/EMIC_DevAgent.Core/Services/Llm/ClaudeLlmService.cs`

Implementar:
- `GenerateAsync()`: Llamada simple a Claude con un prompt
- `GenerateWithContextAsync()`: Prompt con contexto adicional (archivos del SDK)
- `GenerateStructuredAsync<T>()`: Pedir respuesta en JSON y deserializar

**Configuracion:** Leer API key de variable de entorno `ANTHROPIC_API_KEY` o de appsettings.

**Consideraciones:**
- Manejar rate limiting con retry exponencial
- Logging de tokens usados
- Timeout configurable

### 3.3 LlmPromptBuilder.Build()
**Archivo:** `src/EMIC_DevAgent.Core/Services/Llm/LlmPromptBuilder.cs`

Construir prompt estructurado:
```
[System Instructions]
{_systemParts joined}

[Context]
{_contextParts joined}

[User Request]
{_userPrompt}
```

### 3.4 Agregar API key a configuracion
Actualizar `appsettings.json`:
```json
{
  "Llm": {
    "ApiKey": "",  // o usar variable de entorno
    ...
  }
}
```

---

## Fase 4: Templates de Generacion

**Objetivo:** Poder generar archivos .emic, .h, .c a partir de templates.

### 4.1 ITemplateEngine implementacion
Crear `EmicTemplateEngine : ITemplateEngine`:
- `ApplyVariables()`: Reemplazar `.{name}`, `.{pin}`, etc. en templates
- `GenerateFromTemplateAsync()`: Leer template, aplicar variables, retornar GeneratedFile

### 4.2 ApiTemplate
**Archivo:** `src/EMIC_DevAgent.Core/Services/Templates/ApiTemplate.cs`

Generar 3 archivos para una API nueva:
1. **nombre.emic**: Script de generacion con EMIC:setInput, EMIC:copy, EMIC:define
2. **inc/nombre.h**: Header con prototipos y EMIC:ifdef condicionales
3. **src/nombre.c**: Implementacion usando HAL, con init, poll si aplica

**Usar como template base:** Contenido de led.emic/led.h/led.c parametrizado.

### 4.3 DriverTemplate
Generar archivos de driver siguiendo patron ADS1231:
1. **nombre.emic**: Con EMIC:setInput a HAL
2. **inc/nombre.h**: Header con prototipos
3. **src/nombre.c**: Implementacion usando HAL

### 4.4 ModuleTemplate
Generar archivos de modulo siguiendo patron HRD_LoRaWan:
1. **generate.emic**: Script principal que carga APIs y drivers
2. **deploy.emic**: Script de despliegue
3. **m_description.json**: Metadata del modulo

---

## Fase 5: Agentes Principales

**Objetivo:** Implementar la logica de cada agente.

### 5.1 AnalyzerAgent
**Archivo:** `src/EMIC_DevAgent.Core/Agents/AnalyzerAgent.cs`

1. Usar `ISdkScanner.ScanAsync()` para obtener inventario completo
2. Guardar resultado en `context.SdkState`
3. Comparar con el plan de generacion para identificar:
   - Componentes existentes reutilizables
   - Componentes que faltan crear
   - Dependencias que necesitan resolverse

### 5.2 ApiGeneratorAgent
1. Usar `ILlmService` para que Claude genere el contenido especifico
2. Inyectar como contexto los archivos de referencia (led.emic, led.h, led.c)
3. Usar `ApiTemplate` para estructurar la salida
4. Agregar archivos generados a `context.GeneratedFiles`

### 5.3 DriverGeneratorAgent (similar a ApiGeneratorAgent)

### 5.4 ModuleGeneratorAgent (similar, con template de modulo)

### 5.5 RuleValidatorAgent
1. Iterar sobre `context.GeneratedFiles`
2. Ejecutar cada `IValidator` sobre los archivos
3. Guardar resultados en `context.ValidationResults`
4. Si hay fallas, retornar `AgentResult.Failure` con detalles

### 5.6 CompilationAgent
1. Escribir archivos generados al filesystem (TARGET:)
2. Invocar XC16 via `Process.Start()` con `xc16-gcc` o el makefile del proyecto
3. Parsear output con `CompilationErrorParser`
4. Si hay errores, guardar en `context.LastCompilation`
5. Loop de retries: enviar errores a Claude para correccion, re-compilar

### 5.7 ProgramXmlAgent
Generar program.xml necesario para el sistema EMIC.

---

## Fase 6: Orquestador

**Objetivo:** Implementar el flujo completo end-to-end.

### 6.1 OrchestratorAgent
**El agente mas complejo.** Flujo:

```
1. Recibir prompt del usuario
2. Usar Claude para clasificar intent (CreateApi/CreateDriver/CreateModule/...)
3. Si hay ambiguedad → generar DisambiguationQuestions → esperar respuesta
4. Crear GenerationPlan
5. Ejecutar AnalyzerAgent (escanear SDK)
6. Segun intent, ejecutar el generador correspondiente
7. Ejecutar RuleValidatorAgent
8. Si hay fallas de validacion → corregir con Claude → re-validar (max 3 intentos)
9. Ejecutar CompilationAgent
10. Si hay errores → corregir con Claude → re-compilar (max 5 intentos)
11. Actualizar metadata (.emic-meta.json)
12. Generar reporte final
```

### 6.2 OrchestrationPipeline.ExecuteAsync()
Implementar ejecucion secuencial de steps:
```csharp
foreach (var step in _steps.OrderBy(s => s.Order))
{
    if (step.Condition != null && !step.Condition(context))
    {
        step.Status = StepStatus.Skipped;
        continue;
    }
    step.Status = StepStatus.Running;
    step.Result = await step.Agent.ExecuteAsync(context, ct);
    step.Status = step.Result.Status == ResultStatus.Success
        ? StepStatus.Completed
        : StepStatus.Failed;
    if (step.Status == StepStatus.Failed) break;
}
```

---

## Fase 7: Compilacion XC16

**Objetivo:** Integracion real con el compilador.

### 7.1 CompilationService
Crear `Xc16CompilationService : ICompilationService`:
- Detectar instalacion de XC16 en el sistema
- Construir linea de comandos: `xc16-gcc -mcpu=pic24FJ64GA002 ...`
- Ejecutar via `Process.Start()` y capturar stdout/stderr
- Parsear errores con `CompilationErrorParser`

### 7.2 CompilationErrorParser
Parsear formato de error XC16:
```
filename.c:42:10: error: undeclared identifier 'foo'
filename.c:55:1: warning: unused variable 'bar'
```
Regex: `^(.+):(\d+):(\d+):\s+(error|warning):\s+(.+)$`

---

## Fase 8: Testing Integral

### Tests de integracion
- Test end-to-end: prompt "Crear API de temperatura" → archivos generados validos
- Test de validacion: codigo con errores conocidos → detectados correctamente
- Test de escaneo: SDK real → inventario completo y correcto

### Tests unitarios adicionales por fase
Cada fase debe agregar tests para los componentes implementados.

---

## Orden de Prioridad Recomendado

```
FASE 1 (Critica - Fundamentos)
  1.1 EmicFileParser          ← EMPEZAR AQUI
  1.2 SdkPathResolver
  1.3 SdkScanner
  1.4 MetadataService

FASE 2 (Alta - Validacion)
  2.1-2.4 Validadores
  2.5 ValidationService

FASE 3 (Alta - LLM)
  3.1-3.4 ClaudeLlmService + PromptBuilder

FASE 4 (Media - Templates)
  4.1-4.4 TemplateEngine + Templates

FASE 5 (Media - Agentes)
  5.1-5.7 Agentes individuales

FASE 6 (Baja - Orquestacion)
  6.1-6.2 OrchestratorAgent + Pipeline

FASE 7 (Baja - Compilacion)
  7.1-7.2 XC16 integration

FASE 8 (Opcional - Testing)
  Tests de integracion
```

---

## Consideraciones Tecnicas

### Manejo de paths Windows/Unix
El SDK corre en Windows. Usar `Path.Combine()` siempre, nunca concatenar con `/` o `\`.

### Encoding de archivos
Los archivos .emic y .c del SDK pueden tener encoding variado. Usar `Encoding.UTF8` por defecto, con fallback a `Encoding.Default`.

### Performance del escaneo
El SDK tiene cientos de archivos. Considerar cache del `SdkInventory` (serializar a JSON despues del primer scan).

### API Key de Claude
No commitear API keys. Usar variables de entorno o user secrets:
```bash
dotnet user-secrets set "Llm:ApiKey" "sk-ant-..."
```

### Tamanio de contexto LLM
Los archivos del SDK pueden ser grandes. Implementar estrategia de "contexto relevante":
- Solo enviar a Claude los archivos de referencia pertinentes al intent
- Truncar archivos largos a las secciones relevantes
- Usar system prompt con reglas EMIC (de EMIC_Conceptos_Clave.md)
