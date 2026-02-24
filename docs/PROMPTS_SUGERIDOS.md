# EMIC DevAgent - Prompts Sugeridos para Continuar el Desarrollo

> Estos prompts estan disenados para usar en sesiones de Claude Code con contexto limpio.
> Cada prompt es autocontenido y hace referencia a los archivos de documentacion del proyecto.

---

## Contexto Inicial (usar al inicio de cada sesion)

Antes de cualquier prompt de desarrollo, iniciar la sesion con:

```
Lee los archivos de documentacion del proyecto EMIC_DevAgent:
- EMIC_DevAgent/docs/ESTADO_ACTUAL.md
- EMIC_DevAgent/docs/PROXIMOS_PASOS.md
- EMIC_DevAgent/docs/EMIC_Conceptos_Clave.md
- EMIC_DevAgent/docs/architecture.md

El proyecto esta en PIC_XC16/EMIC_DevAgent y es un submodule git.
```

---

## FASE 1: Servicios de Infraestructura

### Prompt 1.1 - EmicFileParser (EMPEZAR AQUI)
```
Implementa EmicFileParser en EMIC_DevAgent/src/EMIC_DevAgent.Core/Services/Sdk/EmicFileParser.cs

Debe parsear archivos .emic del SDK EMIC y extraer:
1. Lineas EMIC:setInput -> lista de dependencias (paths)
2. Lineas EMIC:copy DEV:/source TARGET:/dest -> archivos a copiar
3. Lineas EMIC:define dictName value -> entradas de diccionario
4. Lineas EMIC:ifdef / EMIC:endif -> bloques condicionales

Lee primero estos archivos de referencia reales del SDK para entender el formato:
- PIC_XC16/_api/Indicators/LEDs/led.emic
- PIC_XC16/_drivers/ADC/ADS1231/ADS1231.emic
- PIC_XC16/_api/Actuators/Relay/relay.emic

Crea tests en EMIC_DevAgent/tests/EMIC_DevAgent.Tests/ que parseen los archivos
reales del SDK y verifiquen que las dependencias, copies y defines se extraen bien.

Referencia de conceptos: EMIC_DevAgent/docs/EMIC_Conceptos_Clave.md seccion 3.
```

### Prompt 1.2 - SdkPathResolver
```
Implementa SdkPathResolver.ResolveVolume() en
EMIC_DevAgent/src/EMIC_DevAgent.Core/Services/Sdk/SdkPathResolver.cs

Debe resolver los volumenes logicos EMIC:
- DEV: -> ruta raiz del SDK (SdkPaths.SdkRoot)
- TARGET: -> directorio de generacion (configurable, por defecto SdkRoot/_target)
- SYS: -> directorio de sistema (SdkRoot/_sys)
- USER: -> directorio de usuario (configurable)

Ejemplo: ResolveVolume("DEV:/_api/Indicators/LEDs/led.emic")
retorna "C:\...\PIC_XC16\_api\Indicators\LEDs\led.emic"

Crea tests unitarios para cada volumen.
Lee EMIC_DevAgent/docs/EMIC_Conceptos_Clave.md seccion 3.3 para referencia.
```

### Prompt 1.3 - SdkScanner
```
Implementa SdkScanner en EMIC_DevAgent/src/EMIC_DevAgent.Core/Services/Sdk/SdkScanner.cs

El scanner debe:
1. ScanAsync(): Recorrer recursivamente _api/, _drivers/, _modules/, _hal/
   - Para cada directorio con archivo .emic, usar EmicFileParser para extraer metadata
   - Crear ApiDefinition/DriverDefinition/ModuleDefinition segun la carpeta padre
   - Poblar SdkInventory con todo lo encontrado
2. FindApiAsync(name): Buscar en Apis por nombre
3. FindDriverAsync(name): Buscar en Drivers por nombre
4. FindModuleAsync(name): Buscar en Modules por nombre

El SDK real esta en PIC_XC16/ (directorio padre del submodule).
Usa SdkPathResolver para construir los paths.

Crea tests de integracion que escaneen el SDK real y verifiquen que encuentra
al menos: LEDs, Relay (APIs), ADS1231 (driver), HRD_LoRaWan (modulo).
```

### Prompt 1.4 - MetadataService
```
Implementa MetadataService en
EMIC_DevAgent/src/EMIC_DevAgent.Core/Services/Metadata/MetadataService.cs

Usa System.Text.Json para serializar/deserializar FolderMetadata.
El archivo se llama .emic-meta.json y va en cada carpeta de componente.

Implementa:
- ReadMetadataAsync(): Leer y deserializar, retornar null si no existe
- WriteMetadataAsync(): Serializar con indentacion y escribir
- UpdateHistoryAsync(): Leer existente (o crear nuevo), agregar HistoryEntry, escribir

Crea tests de round-trip: escribir -> leer -> verificar igualdad.
Lee EMIC_DevAgent/docs/architecture.md seccion "Formato de Metadata" para referencia.
```

---

## FASE 2: Validadores

### Prompt 2.1 - LayerSeparationValidator
```
Implementa LayerSeparationValidator en
EMIC_DevAgent/src/EMIC_DevAgent.Core/Agents/Validators/LayerSeparationValidator.cs

Debe analizar archivos .c generados (en context.GeneratedFiles) y detectar:
- Acceso directo a registros: TRISAbits, LATAbits, PORTAbits, TRIS[A-Z], LAT[A-Z], PORT[A-Z]
- Inclusiones directas de headers de hardware (<xc.h>, <p24FJ64GA002.h>, etc.)

Esto solo aplica a archivos en _api/. Los drivers y hal pueden acceder hardware.

Crea tests con:
- Codigo valido (usando HAL_GPIO_*) -> debe pasar
- Codigo invalido (usando TRISAbits) -> debe fallar con issue especifico

Referencia: EMIC_DevAgent/docs/EMIC_Conceptos_Clave.md seccion 2.1
```

### Prompt 2.2 - NonBlockingValidator
```
Implementa NonBlockingValidator en
EMIC_DevAgent/src/EMIC_DevAgent.Core/Agents/Validators/NonBlockingValidator.cs

Debe detectar en archivos .c generados:
- while() con condiciones de espera (excepto while(1) del main loop)
- __delay_ms(), __delay_us(), delay(), _delay()
- for(;;) loops de espera

Crea tests con codigo valido (getSystemMilis + state machine) y codigo
invalido (__delay_ms, while(flag) loops).

Referencia: EMIC_DevAgent/docs/EMIC_Conceptos_Clave.md seccion 2.2
```

### Prompt 2.3 - StateMachineValidator y DependencyValidator
```
Implementa los 2 validadores restantes:

1. StateMachineValidator: Verifica que funciones complejas usan switch(state)
   con variable static y manejo de timeout.

2. DependencyValidator: Verifica que toda referencia EMIC:setInput apunta a
   archivos existentes y no hay dependencias circulares (construir grafo dirigido
   y detectar ciclos con DFS).

Tambien implementa ValidationService.ValidateAllAsync() que ejecuta todos los
IValidator registrados y retorna la lista de ValidationResult.

Crea tests para cada uno. Referencia: EMIC_DevAgent/docs/EMIC_Conceptos_Clave.md
```

---

## FASE 3: Integracion con Claude

### Prompt 3.1 - ClaudeLlmService
```
Implementa ClaudeLlmService en
EMIC_DevAgent/src/EMIC_DevAgent.Core/Services/Llm/ClaudeLlmService.cs

Opciones de implementacion (elegir una):
A) Usar paquete NuGet Anthropic.SDK
B) Usar HttpClient directo contra https://api.anthropic.com/v1/messages

Debe implementar:
- GenerateAsync(): Llamada simple con un prompt
- GenerateWithContextAsync(): Prompt con contexto adicional en system message
- GenerateStructuredAsync<T>(): Pedir respuesta JSON y deserializar

Configuracion:
- API key desde variable de entorno ANTHROPIC_API_KEY o appsettings
- Modelo configurable (default: claude-sonnet-4-20250514)
- MaxTokens y Temperature desde EmicAgentConfig

Incluir retry con backoff exponencial para rate limiting (429).
Agregar API key a appsettings.json como placeholder vacio.

Tambien implementa LlmPromptBuilder.Build() para ensamblar el prompt.
```

---

## FASE 4: Templates

### Prompt 4.1 - Template Engine y ApiTemplate
```
Implementa el sistema de templates para generar archivos del SDK EMIC.

1. Crea EmicTemplateEngine implementando ITemplateEngine:
   - ApplyVariables(): Reemplazar .{name}, .{pin}, etc. en strings
   - GenerateFromTemplateAsync(): Aplicar variables a un template y retornar GeneratedFile

2. Implementa ApiTemplate.GenerateAsync():
   Debe generar 3 archivos para una nueva API:
   - nombre.emic (script de generacion)
   - inc/nombre.h (header con EMIC:ifdef)
   - src/nombre.c (implementacion no-bloqueante)

Lee estos archivos de referencia como patrones base:
- PIC_XC16/_api/Indicators/LEDs/led.emic
- PIC_XC16/_api/Indicators/LEDs/inc/led.h
- PIC_XC16/_api/Indicators/LEDs/src/led.c

El ApiTemplate debe parametrizar estos archivos reemplazando lo especifico
de LEDs por variables genericas.

Crea tests que generen una API ficticia y verifiquen que los archivos
generados tienen la estructura correcta.
```

### Prompt 4.2 - DriverTemplate y ModuleTemplate
```
Implementa DriverTemplate y ModuleTemplate siguiendo el mismo patron que ApiTemplate.

DriverTemplate - Referencia: PIC_XC16/_drivers/ADC/ADS1231/
ModuleTemplate - Referencia: PIC_XC16/_modules/Wireless_Communication/HRD_LoRaWan/System/

Crea tests para cada uno.
```

---

## FASE 5: Agentes

### Prompt 5.1 - AnalyzerAgent
```
Implementa AnalyzerAgent.ExecuteCoreAsync() en
EMIC_DevAgent/src/EMIC_DevAgent.Core/Agents/AnalyzerAgent.cs

Flujo:
1. Leer SdkPath de configuracion
2. Llamar ISdkScanner.ScanAsync() para obtener inventario completo
3. Guardar resultado en context.SdkState
4. Analizar context.Analysis para determinar que componentes existen vs faltan
5. Retornar AgentResult.Success con resumen del analisis

Crea tests que mockeen ISdkScanner y verifiquen el flujo.
```

### Prompt 5.2 - ApiGeneratorAgent
```
Implementa ApiGeneratorAgent.ExecuteCoreAsync() en
EMIC_DevAgent/src/EMIC_DevAgent.Core/Agents/ApiGeneratorAgent.cs

Flujo:
1. Leer context.Analysis para obtener nombre y categoria de la API
2. Leer archivos de referencia del SDK (led.emic, led.h, led.c) como ejemplos
3. Construir prompt para Claude con:
   - System: reglas EMIC de docs/EMIC_Conceptos_Clave.md
   - Context: archivos de referencia
   - User: "Genera API para {nombre} en categoria {categoria}"
4. Llamar ILlmService.GenerateAsync() para obtener contenido
5. Usar ITemplateEngine para estructurar los archivos
6. Agregar a context.GeneratedFiles
7. Retornar AgentResult.Success

Crea tests con mock de ILlmService.
```

### Prompt 5.3 - RuleValidatorAgent y CompilationAgent
```
Implementa los agentes de validacion y compilacion:

1. RuleValidatorAgent.ExecuteCoreAsync():
   - Iterar context.GeneratedFiles
   - Ejecutar cada IValidator
   - Guardar en context.ValidationResults
   - Si hay fallas, retornar Failure con detalles

2. CompilationAgent.ExecuteCoreAsync():
   - Escribir context.GeneratedFiles al filesystem
   - Intentar compilar con XC16 (ICompilationService)
   - Si falla, guardar errores en context.LastCompilation
   - Loop de retries hasta MaxCompilationRetries
   - Retornar Success o Failure

Para CompilationAgent, tambien implementa CompilationErrorParser.Parse()
que parsea output de XC16 con regex: ^(.+):(\d+):(\d+):\s+(error|warning):\s+(.+)$
```

---

## FASE 6: Orquestador

### Prompt 6.1 - OrchestratorAgent (flujo completo)
```
Implementa OrchestratorAgent.ExecuteCoreAsync() y OrchestrationPipeline.ExecuteAsync().

Lee EMIC_DevAgent/docs/architecture.md para el flujo completo.

OrchestratorAgent:
1. Usar Claude para clasificar intent del prompt (CreateApi/CreateDriver/CreateModule)
2. Si ambiguo, crear DisambiguationQuestions y retornar NeedsInput
3. Crear GenerationPlan basado en el intent
4. Armar pipeline: Analyzer -> Generator (segun intent) -> Validator -> Compiler
5. Ejecutar pipeline
6. Si validacion falla, re-generar y re-validar (max 3 intentos)
7. Retornar resultado final

OrchestrationPipeline.ExecuteAsync():
- Ejecutar steps en orden, evaluando condiciones
- Skip steps cuya condicion retorne false
- Parar en el primer step que falle

Crea tests del flujo completo con mocks.
```

---

## FASE 7: Compilacion

### Prompt 7.1 - Integracion con XC16
```
Crea Xc16CompilationService implementando ICompilationService en
EMIC_DevAgent/src/EMIC_DevAgent.Core/Services/Compilation/

Debe:
1. Detectar instalacion de XC16 (buscar xc16-gcc en PATH o rutas estandar)
2. Construir linea de comandos con flags correctos para el MCU configurado
3. Ejecutar compilacion via Process.Start()
4. Capturar stdout/stderr
5. Parsear errores con CompilationErrorParser
6. Retornar CompilationResult

Registrar en DI en Program.cs (reemplazar el throw actual).
```

---

## Prompts de Utilidad

### Verificar estado general
```
Lee EMIC_DevAgent/docs/ESTADO_ACTUAL.md y luego ejecuta:
- dotnet build en EMIC_DevAgent/
- dotnet test en EMIC_DevAgent/
Reporta el estado actual: que compila, que tests pasan, que falta implementar.
```

### Agregar mas tests
```
Lee la estructura actual de EMIC_DevAgent/tests/ y agrega tests unitarios
para todos los componentes implementados que aun no tengan tests.
Cada servicio y agente debe tener al menos 3 tests.
Ejecuta dotnet test para verificar que todos pasan.
```

### Actualizar documentacion
```
Lee todos los archivos .cs del proyecto EMIC_DevAgent/src/ y actualiza
EMIC_DevAgent/docs/ESTADO_ACTUAL.md reflejando que esta implementado
vs que sigue siendo stub. Actualiza la tabla de inventario.
```

### Commit y push
```
Haz commit y push de todos los cambios en EMIC_DevAgent (submodule)
y actualiza la referencia en PIC_XC16. Push ambos repos.
EMIC_DevAgent va a EMIC-Electronics/EMIC_DevAgent branch main.
PIC_XC16 va a EMIC-Electronics/EMIC_IA branch feature/IA-Agent.
```

---

## Notas para el Desarrollador

1. **Siempre leer ESTADO_ACTUAL.md primero** - tiene el inventario actualizado
2. **Seguir el orden de fases** - cada fase depende de la anterior
3. **Tests primero** - escribir tests antes o junto con la implementacion
4. **No romper el build** - cada cambio debe compilar y pasar tests
5. **El SDK esta en el parent** - los archivos de referencia estan en `PIC_XC16/`, no en `EMIC_DevAgent/`
6. **Verificar con `dotnet build && dotnet test`** despues de cada cambio
