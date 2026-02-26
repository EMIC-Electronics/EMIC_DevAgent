# EMIC DevAgent - Arquitectura Completa

## Indice

1. [Vision General](#vision-general)
2. [Menu Inicial de Clasificacion](#menu-inicial-de-clasificacion)
3. [Diagrama de Flujo Principal](#diagrama-de-flujo-principal)
4. [Separacion Core/CLI](#separacion-corecli)
5. [Inyeccion de Dependencias (DI)](#inyeccion-de-dependencias-di)
6. [Framework Base de Agentes](#framework-base-de-agentes)
7. [Agentes del Sistema](#agentes-del-sistema)
   - [OrchestratorAgent](#orchestratoragent)
   - [AnalyzerAgent](#analyzeragent)
   - [ApiGeneratorAgent](#apigeneratoragent)
   - [DriverGeneratorAgent](#drivergeneratoragent)
   - [ModuleGeneratorAgent](#modulegeneratoragent)
   - [ProgramXmlAgent](#programxmlagent)
   - [RuleValidatorAgent](#rulevalidatoragent)
   - [MaterializerAgent](#materializeragent)
   - [CompilationAgent](#compilationagent)
8. [Validadores Especializados](#validadores-especializados)
9. [Servicios](#servicios)
   - [LLM (Claude API)](#llm-claude-api)
   - [SDK Scanner](#sdk-scanner)
   - [Templates](#templates)
   - [Compilacion y SourceMapper](#compilacion-y-sourcemapper)
   - [Metadata](#metadata)
   - [Validation Service](#validation-service)
10. [Modelos de Datos](#modelos-de-datos)
11. [Configuracion](#configuracion)
12. [Implementacion CLI](#implementacion-cli)
13. [Estructura del Proyecto](#estructura-del-proyecto)
14. [Flujo Completo de Ejecucion](#flujo-completo-de-ejecucion)

---

## Vision General

EMIC DevAgent es un agente de IA tipo CLI (similar a Claude Code) para desarrollar componentes del SDK EMIC (modulos, APIs, drivers, HAL). El sistema esta disenado como una **libreria Core pura** (`EMIC_DevAgent.Core`) que puede ser consumida por distintos hosts:

- **CLI** (`EMIC_DevAgent.Cli`) — implementacion actual
- **Web** (`EMIC.Web.IDE`) — futuro, embebido en la aplicacion web via SignalR

El agente recibe un prompt en lenguaje natural del usuario, realiza desambiguacion interactiva LLM-driven, escanea el SDK existente, genera codigo, valida reglas no-negociables, materializa archivos y compila.

---

## Menu Inicial de Clasificacion

Antes de cualquier llamada al LLM, el sistema presenta un **menu interactivo hardcodeado de 2 niveles** que identifica la capa de abstraccion y el sub-tipo. Esto elimina ambiguedad desde el inicio y establece `IntentType` y `ProjectType` sin depender del LLM.

### Nivel 1 — Capa de abstraccion

```
¿Que desea desarrollar?

  1. Sistema distribuido
     Red de dispositivos coordinados: arquitectura maestro/esclavo,
     control descentralizado, monitoreo distribuido, o redes de
     sensores y actuadores que operan de forma conjunta.

  2. Dispositivo integrado
     Equipo funcional autonomo que combina multiples capacidades en un
     solo hardware: controlador, datalogger, monitor remoto, gateway,
     interfaz HMI, o cualquier combinacion de funciones.

  3. Modulo EMIC
     Nodo del ecosistema modular EMIC con una funcion principal
     (sensor, actuador, display, teclado, comunicacion) y funciones
     auxiliares (timers, LEDs). Incluye EMIC-Bus (I2C) por defecto.
     No implementa logica de negocio: es hardware reutilizable cuya
     aplicacion se define en la etapa de integracion.

  4. API EMIC
     Capa de abstraccion de alto nivel que expone funciones, variables
     y eventos para la logica de negocio. Los eventos son callbacks
     declarados pero no implementados, que se resuelven durante la
     integracion. Consume drivers y HAL internamente.

  5. Driver EMIC
     Capa de control de hardware externo al microcontrolador: sensores,
     actuadores, memorias, transceivers, displays. Accede a la capa HAL
     para los recursos internos del micro y expone funciones para ser
     consumidas por la capa API.

  6. Otro (especificar)
```

### Nivel 2 — Sub-tipo (segun seleccion de Nivel 1)

**Opcion 1 → Sistema distribuido:**

| # | Opcion | Descripcion |
|---|--------|-------------|
| 1 | Control de lazo cerrado | Sensores, controlador y actuadores en nodos separados con retroalimentacion en tiempo real |
| 2 | Red de monitoreo | Nodos sensores con concentrador/gateway para recoleccion y visualizacion de datos |
| 3 | Control maestro/esclavo | Nodo central que coordina y comanda nodos perifericos |
| 4 | Sistema hibrido | Combinacion de control, monitoreo y comunicacion distribuida |
| 5 | Otro (especificar) | |

**Opcion 2 → Dispositivo integrado:**

| # | Opcion | Descripcion |
|---|--------|-------------|
| 1 | Controlador | Sistema de control con entradas de sensores y salidas a actuadores |
| 2 | Datalogger | Adquisicion, procesamiento y almacenamiento de datos |
| 3 | Monitor remoto | Sensado de variables con transmision de datos a sistema externo |
| 4 | Gateway / Concentrador | Recibe datos de multiples fuentes, procesa y/o reenvia |
| 5 | Interfaz HMI | Display, teclado y control local para operacion del usuario |
| 6 | Dispositivo multi-funcion | Combinacion personalizada de funciones |
| 7 | Otro (especificar) | |

**Opcion 3 → Modulo EMIC:**

| # | Opcion | Descripcion |
|---|--------|-------------|
| 1 | Sensor | Medicion de magnitud fisica (temperatura, presion, humedad, etc.) |
| 2 | Actuador | Control de dispositivo externo (motor, rele, valvula, calefactor, etc.) |
| 3 | Display | Presentacion visual (LCD, OLED, 7 segmentos, LED matrix, etc.) |
| 4 | Teclado / Entrada | Interfaz de usuario (botones, encoder rotativo, touch, etc.) |
| 5 | Comunicacion | Bridge o gateway de protocolo (RS485, WiFi, Bluetooth, LoRa, etc.) |
| 6 | Indicador | Señalizacion simple (LEDs, buzzer, alarma visual/sonora, etc.) |
| 7 | Otro (especificar) | |

**Opcion 4 → API EMIC:**

| # | Opcion | Descripcion |
|---|--------|-------------|
| 1 | Adquisicion y medicion | Lectura y procesamiento de magnitudes fisicas: abstraccion de ADC, sensores analogicos, celdas de carga. Consume drivers de conversion y sensores. Ej: ADC, LoadCell, AnalogInput. |
| 2 | Comunicacion | Interfaces de comunicacion cableada o inalambrica: USB, I2C, RS485, SPI, WiFi, Bluetooth, LoRa. Gestiona streams de E/S, buffers y parseo de tramas. Ej: USB_API, EMICBus. |
| 3 | Protocolos de aplicacion | Protocolos de alto nivel sobre una capa de comunicacion: Modbus, MQTT, HTTP/REST. Independientes del medio fisico; se montan sobre APIs de comunicacion. Ej: Modbus, DinaModbus. |
| 4 | Temporizado y planificacion | Temporizadores por software con eventos, timeouts, auto-reload. Soporta multiples instancias independientes. Ej: timer_api (multi-instancia con name=). |
| 5 | Indicadores y E/S digital | Abstraccion de perifericos digitales simples: LEDs con patrones de parpadeo, entradas digitales con anti-rebote y eventos, buzzers, señalizacion. Ej: LEDs, DigitalInputs. |
| 6 | Procesamiento de señales | Algoritmos de procesamiento puro sin acceso directo a hardware: filtros digitales (IIR, FIR, promedio movil), conversiones de unidades, deteccion de umbrales, interpolacion. |
| 7 | Control y automatizacion | Lazos de control cerrado (PID), maquinas de estados configurables, secuenciadores, control ON/OFF con histeresis. |
| 8 | Almacenamiento y persistencia | Gestion de datos en memoria no volatil: logging, EEPROM, buffers circulares persistentes, sistema de archivos en SD, parametros de configuracion. |
| 9 | Otro (especificar) | |

#### Nivel 3 — Sub-tipo de API (segun seleccion de Nivel 2)

**API 4.1 → Adquisicion y medicion:**

| # | Opcion | Descripcion |
|---|--------|-------------|
| 1 | Temperatura | Sensor de temperatura: analogico (LM35, NTC), digital (DS18B20), termopar (MAX6675), RTD (PT100) |
| 2 | Presion / Fuerza / Peso | Celda de carga (HX711, ADS1231), sensor de presion, galga extensometrica |
| 3 | Humedad / Ambiente | Sensor ambiental: humedad (DHT22, SHT30), calidad de aire, luminosidad (LDR, BH1750) |
| 4 | Posicion / Movimiento | Encoder incremental, acelerometro (MPU6050), giroscopio, sensor de proximidad |
| 5 | Corriente / Voltaje | Medicion electrica: sensor de corriente (ACS712), divisor de voltaje, medicion de potencia |
| 6 | ADC generico | Conversion analogico-digital generica configurable, multi-canal, con PGA opcional |
| 7 | Otro (especificar) | |

**API 4.2 → Comunicacion:**

| # | Opcion | Descripcion |
|---|--------|-------------|
| 1 | Serial / USB | Comunicacion serial asincrona via UART o USB-UART bridge (MCP2200, FT232, CP2102) |
| 2 | I2C / EMIC-Bus | Bus I2C para comunicacion entre modulos EMIC o con perifericos externos |
| 3 | SPI | Bus SPI para perifericos de alta velocidad (memorias Flash, ADC externos, displays) |
| 4 | RS485 / RS232 | Comunicacion industrial cableada half/full duplex |
| 5 | WiFi | Comunicacion inalambrica TCP/IP via modulo WiFi (ESP8266, ESP32) |
| 6 | Bluetooth / BLE | Comunicacion inalambrica de corto alcance (HC-05, HM-10, ESP32-BLE) |
| 7 | LoRa / SubGHz | Comunicacion inalambrica de largo alcance y bajo consumo (SX1278, SX1262) |
| 8 | Otro (especificar) | |

**API 4.3 → Protocolos de aplicacion:**

| # | Opcion | Descripcion |
|---|--------|-------------|
| 1 | Modbus RTU/TCP | Protocolo industrial Modbus sobre serial (RTU) o TCP/IP. Master o Slave. |
| 2 | MQTT | Protocolo pub/sub para IoT sobre TCP/IP. Requiere API de comunicacion WiFi/Ethernet. |
| 3 | HTTP / REST | Servidor o cliente HTTP para APIs REST. Requiere stack TCP/IP. |
| 4 | Protocolo EMIC-Bus | Protocolo nativo de comunicacion entre modulos EMIC sobre I2C. Tags + mensajes. |
| 5 | Protocolo propietario | Protocolo de aplicacion personalizado sobre cualquier medio de transporte. |
| 6 | Otro (especificar) | |

**API 4.4 → Temporizado y planificacion:**

| # | Opcion | Descripcion |
|---|--------|-------------|
| 1 | Timer de eventos | Temporizador con callback al expirar. Modos: timer unico (T) y auto-reload (A). |
| 2 | Scheduler / Planificador | Ejecuta tareas a intervalos regulares configurables. Multiples slots independientes. |
| 3 | Timeout / Watchdog SW | Deteccion de inactividad o timeout con accion de recuperacion configurable. |
| 4 | Generador de pulsos | Generacion de señales periodicas o unicas por software (no PWM de hardware). |
| 5 | Otro (especificar) | |

**API 4.5 → Indicadores y E/S digital:**

| # | Opcion | Descripcion |
|---|--------|-------------|
| 1 | LEDs / Indicadores luminosos | Control de LEDs con patrones: encendido, parpadeo, secuencias, dimming por software. |
| 2 | Botones / Teclado | Entradas digitales con anti-rebote, deteccion de pulsacion corta/larga, matriz de teclas. |
| 3 | Encoder rotativo | Lectura de encoder incremental con deteccion de direccion y pulsador integrado. |
| 4 | Buzzer / Alarma sonora | Generacion de tonos y patrones sonoros para señalizacion. |
| 5 | Rele / Salida digital | Control de salidas digitales de potencia con temporizacion opcional. |
| 6 | Otro (especificar) | |

**API 4.6 → Procesamiento de señales:**

| # | Opcion | Descripcion |
|---|--------|-------------|
| 1 | Filtro digital (IIR/FIR/promedio) | Filtrado de señales: paso bajo, paso alto, paso banda, promedio movil, mediana. |
| 2 | Conversion y calibracion | Conversion de unidades, linealizacion por tabla/polinomio, calibracion multi-punto. |
| 3 | Deteccion de umbrales y alarmas | Cruce de umbral con histeresis, deteccion de picos, alarmas configurables. |
| 4 | Transformada / Analisis espectral | FFT, analisis de frecuencia, descomposicion de señales periodicas. |
| 5 | Otro (especificar) | |

**API 4.7 → Control y automatizacion:**

| # | Opcion | Descripcion |
|---|--------|-------------|
| 1 | PID | Controlador PID con anti-windup, limites de salida y parametros configurables en runtime. |
| 2 | Maquina de estados configurable | Motor de estados/transiciones con eventos, condiciones y acciones programables. |
| 3 | Secuenciador | Ejecucion secuencial de pasos con tiempos, condiciones de avance y bucles. |
| 4 | Control ON/OFF con histeresis | Control todo/nada con banda muerta configurable y proteccion de ciclo minimo. |
| 5 | Otro (especificar) | |

**API 4.8 → Almacenamiento y persistencia:**

| # | Opcion | Descripcion |
|---|--------|-------------|
| 1 | Buffer circular / FIFO | Almacenamiento temporal en RAM con gestion de desborde y lectura por lotes. |
| 2 | Logging a memoria | Registro de eventos o datos con timestamp en memoria no volatil. |
| 3 | EEPROM / Flash | Lectura/escritura de parametros de configuracion en memoria no volatil interna. |
| 4 | Tarjeta SD / Sistema de archivos | Almacenamiento masivo con sistema de archivos FAT16/FAT32. |
| 5 | Otro (especificar) | |

**Opcion 5 → Driver EMIC:**

| # | Opcion | Descripcion |
|---|--------|-------------|
| 1 | Sensor | Chip sensor (LM35, BME280, MPU6050, MAX6675, etc.) |
| 2 | Conversor | ADC/DAC externo, expansor de I/O (MCP3008, PCF8574, etc.) |
| 3 | Memoria | EEPROM, Flash SPI, tarjeta SD, FRAM, etc. |
| 4 | Display | Controlador de display (HD44780, SSD1306, MAX7219, etc.) |
| 5 | Transceptor / Comunicacion | Modulo de comunicacion (MCP2200, ESP8266, SX1278, HC-05, etc.) |
| 6 | Motor / Actuador | Driver de potencia (L298, DRV8825, ULN2003, etc.) |
| 7 | Otro (especificar) | |

### Mapeo Menu → Enums

| Nivel 1 | → IntentType | → ProjectType |
|---------|-------------|---------------|
| Sistema distribuido | CreateModule | DistributedSystem |
| Dispositivo integrado | CreateModule | Monolithic |
| Modulo EMIC | CreateModule | EmicModule |
| API EMIC | CreateApi | (no aplica) |
| Driver EMIC | CreateDriver | (no aplica) |

| Nivel 2 (Modulo EMIC) | → ModuleRole |
|----------------------|-------------|
| Sensor | Sensor |
| Actuador | Actuator |
| Display | Display |
| Teclado/Entrada | Input |
| Comunicacion | Communication |
| Indicador | Indicator |

| Nivel 2 (Sistema distribuido) | → SystemKind |
|-------------------------------|-------------|
| Control de lazo cerrado | ClosedLoopControl |
| Red de monitoreo | Monitoring |
| Control maestro/esclavo | RemoteControl |
| Sistema hibrido | Combined |

| Nivel 2 (Dispositivo integrado) | → DeviceFunction |
|--------------------------------|-----------------|
| Controlador | Controller |
| Datalogger | Datalogger |
| Monitor remoto | RemoteMonitor |
| Gateway | Gateway |
| Interfaz HMI | Hmi |
| Multi-funcion | MultiFunction |

| Nivel 2 (API EMIC) | → ApiType |
|--------------------|----------|
| Adquisicion y medicion | Measurement (*) |
| Comunicacion | Communication (*) |
| Protocolos de aplicacion | ApplicationProtocol (*) |
| Temporizado y planificacion | Timing (*) |
| Indicadores y E/S digital | DigitalIO (*) |
| Procesamiento de señales | SignalProcessing |
| Control y automatizacion | Control |
| Almacenamiento y persistencia | Storage |

(*) Valores nuevos que requieren actualizar el enum `ApiType` en `AgentContext.cs`.

| Nivel 2 (Driver EMIC) | → DriverTarget |
|-----------------------|---------------|
| Sensor | Sensor |
| Conversor | Converter |
| Memoria | Memory |
| Display | Display |
| Transceptor | Transceiver |
| Motor / Actuador | MotorActuator |

### Implementacion

El menu se ejecuta en `OrchestratorAgent.RunInitialMenuAsync()` **antes** de `ClassifyIntentAsync()`. Los resultados se almacenan en `context.Properties` con claves `MenuIntent`, `MenuProjectType`, `MenuModuleRole`, `MenuSystemKind`, `MenuDeviceFunction`, `MenuApiType`, `MenuDriverTarget`. Luego se pasan como historial de conversacion al LLM para que no re-pregunte lo ya resuelto.

---

## Diagrama de Flujo Principal

```
PROMPT USUARIO
    |
    v
[OrchestratorAgent]
    |-- 1. RunInitialMenuAsync (hardcoded, 2 niveles)
    |       |-- Nivel 1: Capa de abstraccion (6 opciones)
    |       |-- Nivel 2: Sub-tipo (segun seleccion)
    |       → IntentType, ProjectType, ModuleRole/SystemKind/DeviceFunction/ApiType/DriverTarget
    |
    |-- 2. ClassifyIntentAsync (LLM ligero → key=value)
    |       → PromptAnalysis { ComponentName, Category, Description }
    |       (Intent viene del menu, no del LLM)
    |
    |-- 3. RunDisambiguationAsync (LLM-driven, detalles tecnicos)
    |       |-- Recibe historial del menu (no re-pregunta)
    |       |-- LLM genera QUESTION o COMPLETE
    |       |-- Loop max 10 iteraciones
    |       → DetailedSpecification { ProjectType, ModuleRole, ... }
    |
    |-- 4. DisplaySpecificationAsync (muestra spec al usuario)
    |
    |-- 5. [Si DisambiguationOnly] → STOP aqui
    |
    |-- 6. SDK Scan (DESPUES de desambiguacion)
    |       → SdkInventory { Apis, Drivers, Modules, HalComponents }
    |
    |-- 7. CreateGenerationPlan (top-down)
    |       → GenerationPlan { FilesToGenerate, Dependencies }
    |
    |-- 8. Ejecuta sub-agentes bottom-up (segun intent):
    |
    v
[AnalyzerAgent]
    |-- Escanea SDK, encuentra reutilizables, identifica gaps
    |
    v
[Decision segun intent]
    |-- CreateModule ──► DriverGenerator → ApiGenerator → ModuleGenerator → ProgramXml
    |-- CreateApi    ──► ApiGenerator
    |-- CreateDriver ──► DriverGenerator
    |
    v
[RuleValidatorAgent]
    |-- LayerSeparation: PASS/FAIL
    |-- NonBlocking: PASS/FAIL
    |-- StateMachine: PASS/FAIL
    |-- Dependencies: PASS/FAIL
    |-- BackwardsCompatibility: PASS/FAIL
    |
    v
[MaterializerAgent]
    |-- Escribe archivos a disco (via MediaAccess virtual paths)
    |-- Ejecuta TreeMaker.Generate() → codigo C expandido + .map files
    |
    v
[CompilationAgent]
    |-- Compila con XC16
    |-- Si errores: parsea, retropropaga via SourceMapper, intenta fix
    |-- Max 5 reintentos
    |
    v
REPORTE FINAL AL USUARIO
```

---

## Separacion Core/CLI

### Principio de diseno

`EMIC_DevAgent.Core` es una **libreria pura sin dependencia a ningun host**. Define interfaces que cada host implementa:

```
+-------------------+     +---------------------------+
| EMIC_DevAgent.Cli |     | EMIC.Web.IDE              |
|   (Console host)  |     |   (ASP.NET host, futuro)  |
|                   |     |                           |
| ConsoleInteraction|     | SignalRInteraction        |
| CliAgentSession   |     | WebAgentSession           |
| ConsoleEventSink  |     | SignalREventSink          |
| ClaudeLlmService  |     | ClaudeLlmService          |
+--------+----------+     +------------+--------------+
         |                              |
         |   ProjectReference           |   ProjectReference
         v                              v
+--------------------------------------------------+
|              EMIC_DevAgent.Core                   |
|                                                  |
|  Interfaces que el host debe implementar:        |
|    IUserInteraction, IAgentSession,              |
|    IAgentEventSink, ILlmService,                 |
|    ICompilationService                           |
|                                                  |
|  Extension: services.AddEmicDevAgent(config)     |
|  Registra: Agentes, Validadores, Templates,      |
|            Scanner, Parser, MediaAccess           |
+--------------------------------------------------+
```

### Interfaces de abstraccion

| Interfaz | Archivo | Proposito | CLI implementa | Web implementara |
|----------|---------|-----------|----------------|------------------|
| `IUserInteraction` | `Agents/Base/IUserInteraction.cs` | Preguntas, confirmaciones, progreso | `ConsoleUserInteraction` | `SignalRUserInteraction` |
| `IAgentSession` | `Configuration/IAgentSession.cs` | Contexto usuario (email, SDK path, virtual drivers) | `CliAgentSession` | `WebAgentSession` |
| `IAgentEventSink` | `Agents/Base/IAgentEventSink.cs` | Eventos en tiempo real (steps, archivos, compilacion) | `ConsoleEventSink` | `SignalREventSink` |
| `ILlmService` | `Services/Llm/ILlmService.cs` | Generacion de texto con LLM | `ClaudeLlmService` | `ClaudeLlmService` |
| `ICompilationService` | `Services/Compilation/ICompilationService.cs` | Compilacion XC16 | `EmicCompilationService` | `EmicCompilationService` |

---

## Inyeccion de Dependencias (DI)

### Punto de entrada: `ServiceCollectionExtensions.AddEmicDevAgent(config)`

**Archivo**: `Configuration/ServiceCollectionExtensions.cs`

Este extension method registra TODOS los servicios internos de Core. Cada host solo agrega sus implementaciones especificas.

```csharp
// En CLI Program.cs:
services.AddEmicDevAgent(config);                           // Registra todo Core
services.AddSingleton<IUserInteraction, ConsoleUserInteraction>();
services.AddSingleton<IAgentSession, CliAgentSession>();
services.AddSingleton<IAgentEventSink, ConsoleEventSink>();
services.AddScoped<ILlmService>(sp => sp.GetRequiredService<ClaudeLlmService>());
services.AddScoped<ICompilationService, EmicCompilationService>();
```

### Registro completo de servicios

| Servicio | Lifetime | Razon |
|----------|----------|-------|
| `EmicAgentConfig` | Singleton | Configuracion inmutable |
| `SdkPaths` | Singleton | Paths calculados, stateless |
| `IValidator` (x5) | Singleton | Validadores stateless |
| `ApiTemplate`, `DriverTemplate`, `ModuleTemplate` | Singleton | Templates stateless |
| `ITemplateEngine` → `TemplateEngineService` | Singleton | Stateless |
| `CompilationErrorParser` | Singleton | Stateless |
| `SourceMapper` | Singleton | Stateless |
| `LlmPromptBuilder` | Transient | Builder pattern, instancia fresca cada vez |
| `MediaAccess` | Scoped | Depende de IAgentSession (user email + virtual drivers) |
| `SdkPathResolver` | Scoped | Usa MediaAccess scoped |
| `EmicFileParser` | Scoped | Usa MediaAccess scoped |
| `ISdkScanner` → `SdkScanner` | Scoped | Cache por request |
| `IMetadataService` → `MetadataService` | Scoped | Usa MediaAccess scoped |
| `ValidationService` | Scoped | Agrega validadores |
| Todos los agentes | Scoped | Estado por request |
| `OrchestratorAgent` | Scoped (factory) | Factory que resuelve sub-agents del scope |
| `IAgentEventSink` → `NullAgentEventSink` | Singleton (fallback) | `TryAddSingleton` — solo si el host no registra uno |

### Factory de OrchestratorAgent

El `OrchestratorAgent` se registra con un **factory delegate** que construye la lista de sub-agentes manualmente:

```csharp
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
        sp.GetRequiredService<ISdkScanner>(),
        sp.GetRequiredService<ILogger<OrchestratorAgent>>());
});
```

### Grafo de dependencias

```
OrchestratorAgent
├── ILlmService ─────────────── (host provee)
├── IUserInteraction ────────── (host provee)
├── ISdkScanner
│   ├── MediaAccess
│   │   └── IAgentSession ──── (host provee)
│   └── ILogger<SdkScanner>
├── IEnumerable<IAgent> (sub-agents):
│   ├── AnalyzerAgent
│   │   ├── ISdkScanner (compartido en el scope)
│   │   └── ILogger<AnalyzerAgent>
│   ├── ApiGeneratorAgent
│   │   ├── ILlmService
│   │   ├── ITemplateEngine
│   │   └── ILogger<ApiGeneratorAgent>
│   ├── DriverGeneratorAgent
│   │   ├── ILlmService
│   │   ├── ITemplateEngine
│   │   └── ILogger<DriverGeneratorAgent>
│   ├── ModuleGeneratorAgent
│   │   ├── ILlmService
│   │   ├── ITemplateEngine
│   │   └── ILogger<ModuleGeneratorAgent>
│   ├── ProgramXmlAgent
│   │   ├── ILlmService
│   │   └── ILogger<ProgramXmlAgent>
│   ├── RuleValidatorAgent
│   │   ├── IEnumerable<IValidator> (5 validadores singleton)
│   │   └── ILogger<RuleValidatorAgent>
│   ├── MaterializerAgent
│   │   ├── MediaAccess
│   │   ├── IAgentSession
│   │   └── ILogger<MaterializerAgent>
│   └── CompilationAgent
│       ├── ICompilationService ── (host provee)
│       ├── CompilationErrorParser
│       ├── SourceMapper
│       ├── EmicAgentConfig
│       ├── MediaAccess
│       └── ILogger<CompilationAgent>
└── ILogger<OrchestratorAgent>
```

---

## Framework Base de Agentes

### IAgent (`Agents/Base/IAgent.cs`)

Contrato que TODOS los agentes deben implementar:

```csharp
public interface IAgent
{
    string Name { get; }                    // Identificador del agente
    string Description { get; }             // Descripcion legible
    bool CanHandle(AgentContext context);    // ¿Puede procesar este contexto?
    Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct);
}
```

### AgentBase (`Agents/Base/AgentBase.cs`)

Clase abstracta que implementa `IAgent` con wrapper de excepciones y logging:

- **Constructor**: `AgentBase(ILogger logger)` — almacena `Logger` como protected
- **ExecuteAsync()**: Wrapper publico que llama a `ExecuteCoreAsync()` dentro de try/catch. Si hay excepcion, retorna `AgentResult.Failure` con el mensaje de error
- **ExecuteCoreAsync()**: Metodo abstracto que cada agente sobreescribe con su logica
- **CanHandle()**: Abstracto — cada agente decide si puede manejar el contexto actual
- **Name / Description**: Propiedades abstractas de solo lectura

### AgentContext (`Agents/Base/AgentContext.cs`)

Objeto compartido que viaja entre TODOS los agentes del pipeline. Es el "bus de datos" del sistema:

| Propiedad | Tipo | Descripcion |
|-----------|------|-------------|
| `OriginalPrompt` | `string` | Prompt del usuario (inmutable una vez seteado) |
| `Analysis` | `PromptAnalysis?` | Clasificacion del intent (paso 1 del orchestrator) |
| `SdkState` | `SdkInventory?` | Inventario completo del SDK (despues del scan) |
| `Plan` | `GenerationPlan?` | Plan de generacion con archivos a crear |
| `Specification` | `DetailedSpecification?` | Especificacion completa despues de desambiguacion |
| `DisambiguationOnly` | `bool` | Si `true`, el pipeline se detiene despues de mostrar la spec |
| `GeneratedFiles` | `List<GeneratedFile>` | Archivos generados por los agentes (in-memory) |
| `PendingQuestions` | `List<DisambiguationQuestion>` | Preguntas pendientes de desambiguacion |
| `ValidationResults` | `List<ValidationResult>` | Resultados de cada validador |
| `LastCompilation` | `CompilationResult?` | Resultado de la ultima compilacion |
| `Properties` | `Dictionary<string, object>` | Datos dinamicos entre agentes |

**Claves conocidas de Properties**:
- `"SdkPath"` → string: ruta al SDK (seteada por CLI)
- `"ProjectPath"` → string: ruta DEV: del modulo (seteada por MaterializerAgent)
- `"SystemPath"` → string: ruta DEV: al System del modulo (seteada por MaterializerAgent)
- `"ReusableApis"` → `List<ApiDefinition>`: APIs reutilizables (seteada por AnalyzerAgent)
- `"ReusableDrivers"` → `List<DriverDefinition>`: drivers reutilizables (seteada por AnalyzerAgent)
- `"ReusableModules"` → `List<ModuleDefinition>`: modulos reutilizables (seteada por AnalyzerAgent)
- `"Gaps"` → `List<string>`: componentes que faltan crear (seteada por AnalyzerAgent)
- `"InterchangeabilityIssues"` → `List<string>`: warnings de compatibilidad entre drivers (seteada por AnalyzerAgent)

### AgentResult (`Agents/Base/AgentResult.cs`)

Resultado estandarizado de ejecucion de un agente:

| Propiedad | Tipo | Descripcion |
|-----------|------|-------------|
| `AgentName` | `string` | Nombre del agente que genero el resultado |
| `Status` | `ResultStatus` | `Success`, `Failure`, `NeedsInput`, `Partial` |
| `Message` | `string` | Mensaje descriptivo del resultado |
| `Data` | `Dictionary<string, object>` | Datos adicionales del resultado |

**Factory methods estaticos**:
- `AgentResult.Success(agentName, message)` → status = Success
- `AgentResult.Failure(agentName, message)` → status = Failure
- `AgentResult.NeedsInput(agentName, message)` → status = NeedsInput

### AgentMessage (`Agents/Base/AgentMessage.cs`)

Estructura para mensajeria inter-agente (uso minimo actual, preparado para futuro):

| Propiedad | Tipo | Descripcion |
|-----------|------|-------------|
| `FromAgent` | `string` | Agente emisor |
| `ToAgent` | `string` | Agente receptor |
| `Type` | `MessageType` | `Request`, `Response`, `Error`, `Progress`, `Question` |
| `Content` | `string` | Cuerpo del mensaje |
| `Data` | `Dictionary<string, object>` | Payload adicional |
| `Timestamp` | `DateTime` | Marca temporal |

### IUserInteraction (`Agents/Base/IUserInteraction.cs`)

Abstraccion de interaccion con el usuario:

```csharp
public interface IUserInteraction
{
    Task<string> AskQuestionAsync(DisambiguationQuestion question, CancellationToken ct);
    Task ReportProgressAsync(string agentName, string message, double? progressPercent, CancellationToken ct);
    Task<bool> ConfirmActionAsync(string description, CancellationToken ct);
}
```

### IAgentEventSink (`Agents/Base/IAgentEventSink.cs`)

Publica eventos para UI/logging en tiempo real:

```csharp
public interface IAgentEventSink
{
    Task OnStepStarted(string stepName, string agentName, CancellationToken ct);
    Task OnStepCompleted(string stepName, AgentResult result, CancellationToken ct);
    Task OnFileGenerated(GeneratedFile file, CancellationToken ct);
    Task OnValidationResult(ValidationResult result, CancellationToken ct);
    Task OnCompilationResult(CompilationResult result, CancellationToken ct);
}
```

---

## Agentes del Sistema

### OrchestratorAgent

**Archivo**: `Agents/OrchestratorAgent.cs`
**Rol**: Coordinador principal de todo el pipeline
**Name**: `"Orchestrator"`
**CanHandle**: Siempre retorna `true`

**Constructor**:
```csharp
OrchestratorAgent(
    ILlmService _llmService,
    IUserInteraction _userInteraction,
    IEnumerable<IAgent> _subAgents,
    ISdkScanner _sdkScanner,
    ILogger<OrchestratorAgent> logger)
```

**ExecuteCoreAsync — Flujo completo**:

1. **ClassifyIntentAsync(prompt)**: Llamada LLM ligera que extrae intent, componentName, category, description, dependencies en formato `key=value`. Usa `ParseIntentResponse()` para parsear la respuesta.

2. **RunDisambiguationAsync(context)**: Loop interactivo LLM-driven con max 10 iteraciones:
   - **Fase 0 (hardcoded)**: Siempre pregunta "¿Que tipo de proyecto desea crear?" con opciones fijas: Proyecto monolitico, Modulo EMIC, Sistema distribuido, Otro. Se ejecuta ANTES del loop LLM.
   - **Loop LLM (Fases 1-3)**: En cada iteracion, `GenerateLlmDisambiguationStepAsync()` envia al LLM: el prompt original, la clasificacion inicial, y toda la conversacion previa. El LLM responde en formato COMPLETE+JSON o QUESTION+OPTIONS+REASON.
   - **ParseDisambiguationResponse()**: Parsea la respuesta del LLM. Si es COMPLETE, extrae el JSON de especificacion. Si es QUESTION, extrae pregunta, opciones y razon.
   - **SplitOptionsRespectingParentheses()**: Divide opciones por comas respetando parentesis. "Sensor digital (DS18B20, SHT30), Otro" → 2 opciones, no 4.
   - **ForceFinalSpecificationAsync()**: Si se alcanzan las 10 iteraciones, fuerza al LLM a emitir COMPLETE best-effort. Si falla, construye spec desde PromptAnalysis.

3. **DisplaySpecificationAsync()**: Muestra la spec formateada al usuario via `ReportProgressAsync`.

4. **Si `DisambiguationOnly` == true**: Retorna Success sin continuar. Este es el modo actual en la CLI.

5. **SDK Scan**: Llama a `_sdkScanner.ScanAsync()` DESPUES de la desambiguacion (para no contaminar preguntas con componentes existentes).

6. **CreateGenerationPlan()**: Crea plan top-down con archivos a generar segun intent:
   - `CreateApi` → `.emic`, `.h`, `.c` en `_api/{category}/{name}/`
   - `CreateDriver` → `.emic`, `.h`, `.c` en `_drivers/{category}/{name}/`
   - `CreateModule` → `generate.emic`, `deploy.emic`, `m_description.json` en `_modules/{category}/{name}/`

7. **DetermineAgentSequence()**: Determina el orden de ejecucion bottom-up:
   - `CreateModule` → Analyzer → DriverGenerator → ApiGenerator → ModuleGenerator → ProgramXml → RuleValidator → Materializer → Compilation
   - `CreateApi` → Analyzer → ApiGenerator → RuleValidator → Materializer → Compilation
   - `CreateDriver` → Analyzer → DriverGenerator → RuleValidator → Materializer → Compilation

8. **Ejecucion secuencial**: Itera los sub-agentes. Si uno falla (status Failure), el pipeline se detiene (excepto RuleValidator con solo warnings). Si NeedsInput, retorna al usuario.

**System prompt de desambiguacion** (`GetDisambiguationSystemPrompt()`):
- Define contexto EMIC (3 tipos de proyecto: monolitico, modulo EMIC, sistema distribuido)
- Establece 4 fases: tipo de proyecto → rol del componente → especificacion tecnica → detalles de integracion
- 9 reglas criticas: un tema por pregunta, siempre "Otro (especificar)", modulo EMIC implica I2C, no mencionar SDK, minimo 3-4 preguntas, mismo idioma del usuario, seguir orden de fases, no repetir, incorporar info extra
- Define 2 formatos de respuesta: COMPLETE+JSON o QUESTION+OPTIONS+REASON

**ParseSpecificationJson()**: Parser JSON manual (sin System.Text.Json) que extrae todos los campos de `DetailedSpecification` incluyendo enums (`ProjectType`, `ModuleRole`, `SystemKind`, `IntentType`), strings, arrays y `additionalDetails` como sub-objeto.

---

### AnalyzerAgent

**Archivo**: `Agents/AnalyzerAgent.cs`
**Rol**: Escanea el SDK, encuentra componentes reutilizables e identifica gaps
**Name**: `"Analyzer"`
**CanHandle**: `context.Analysis != null` (necesita la clasificacion del intent)

**Constructor**:
```csharp
AnalyzerAgent(ISdkScanner _sdkScanner, ILogger<AnalyzerAgent> logger)
```

**ExecuteCoreAsync**:

1. **SDK Inventory**: Si `context.SdkState` ya tiene datos (pre-poblado por el orchestrator), los reutiliza. Si no, escanea via `_sdkScanner.ScanAsync()`.

2. **Busqueda de reutilizables**:
   - `FindMatchingApis()`: Busca APIs por nombre de componente, categoria, o dependencies requeridas
   - `FindMatchingDrivers()`: Idem para drivers
   - `FindMatchingModules()`: Idem para modulos

3. **Identificacion de gaps**: Segun el intent:
   - `CreateApi`: ¿Existe la API con ese nombre? Si no → gap
   - `CreateDriver`: ¿Existe el driver con ese nombre? Si no → gap
   - `CreateModule`: ¿Existe el modulo? + ¿Existen todas las dependencias? Si no → gap

4. **CheckDriverInterchangeability()**: Agrupa drivers por categoria y verifica que drivers de la misma categoria tengan las mismas funciones (ej: todos los sensores de temperatura deben tener `getTemperature`). Reporta diferencias como warnings.

5. **Almacena resultados** en `context.Properties`: `ReusableApis`, `ReusableDrivers`, `ReusableModules`, `Gaps`, `InterchangeabilityIssues`.

---

### ApiGeneratorAgent

**Archivo**: `Agents/ApiGeneratorAgent.cs`
**Rol**: Genera archivos .emic, .h, .c para nuevas APIs
**Name**: `"ApiGenerator"`
**CanHandle**: `context.Analysis?.Intent == CreateApi` o (es `CreateModule` y hay gaps de API)

**Constructor**:
```csharp
ApiGeneratorAgent(ILlmService _llmService, ITemplateEngine _templateEngine, ILogger<ApiGeneratorAgent> logger)
```

**ExecuteCoreAsync**:

1. **ResolveVariablesAsync()**: Usa LLM para extraer: nombre de la API, pin por defecto, dependencia HAL necesaria, funciones a exponer.

2. **Generacion de archivos** usando templates + LLM:
   - `GenerateEmicFile()`: Genera archivo `.emic` con directivas `EMIC:setInput`, `EMIC:define(inits.*)`, `EMIC:define(polls.*)`, `EMIC:define(c_modules.*)`, `EMIC:define(main_includes.*)`
   - `GenerateHeaderFile()`: Genera `.h` con `#ifndef` guards, `#include` necesarios, declaraciones de funciones
   - **Codigo fuente**: Usa LLM via `EnhanceWithLlmAsync()` para generar la implementacion `.c`. Si LLM falla, usa `GenerateDefaultSource()` como fallback con stubs vacios

3. **Agrega** 3 `GeneratedFile` a `context.GeneratedFiles` con paths relativos `_api/{category}/{name}/`

---

### DriverGeneratorAgent

**Archivo**: `Agents/DriverGeneratorAgent.cs`
**Rol**: Genera drivers para chips externos (.emic, .h, .c) usando HAL
**Name**: `"DriverGenerator"`
**CanHandle**: `context.Analysis?.Intent == CreateDriver` o (es `CreateModule` y hay gaps de driver)

**Constructor**: Mismo que ApiGeneratorAgent (`ILlmService`, `ITemplateEngine`, `ILogger`)

**ExecuteCoreAsync**:

1. **ResolveVariablesAsync()**: Usa LLM para extraer: tipo de chip, dependencia HAL (SPI, I2C, ADC, etc.), funciones que expone

2. **GenerateDriverFilesAsync()**: Usa LLM para generar implementacion completa del driver:
   - `.emic`: Directivas EMIC, `EMIC:setInput` al HAL correspondiente, registra `c_modules`
   - `.h`: Declaraciones de funciones (init, read, write, getTemperature, etc.)
   - `.c`: Implementacion usando funciones HAL_* (NO acceso directo a registros)

3. **Agrega** 3 `GeneratedFile` a `context.GeneratedFiles` con paths `_drivers/{category}/{name}/`

---

### ModuleGeneratorAgent

**Archivo**: `Agents/ModuleGeneratorAgent.cs`
**Rol**: Genera modulos EMIC completos (generate.emic, deploy.emic, m_description.json)
**Name**: `"ModuleGenerator"`
**CanHandle**: `context.Analysis?.Intent == CreateModule`

**Constructor**: Mismo que ApiGeneratorAgent (`ILlmService`, `ITemplateEngine`, `ILogger`)

**ExecuteCoreAsync**:

1. **ResolveVariablesAsync()**: Usa LLM para extraer: nombre del PCB, features, aplicaciones del modulo

2. **Identifica APIs/drivers necesarios** revisando los `GeneratedFiles` ya creados por agentes anteriores en el pipeline

3. **GenerateGenerateEmic()**: Crea `generate.emic` con:
   - `EMIC:setInput` al PCB header
   - `EMIC:setInput` a cada API necesaria (con parametros: driver, port, etc.)
   - `EMIC:setInput` a _system includes
   - `EMIC:setInput` a _main
   - Includes dinamicos basados en el inventario

4. **GenerateDeployEmic()**: Crea `deploy.emic` con:
   - Configuracion de EMIC-TABS (Resources, Data)
   - Paths a recursos del driver

5. **GenerateDescriptionJson()**: Crea `m_description.json` con:
   - Metadata del modulo (nombre, categoria, descripcion, version)
   - Hardware requirements
   - APIs y drivers requeridos

6. **Agrega** 3 `GeneratedFile` a `context.GeneratedFiles` con paths `_modules/{category}/{name}/`

---

### ProgramXmlAgent

**Archivo**: `Agents/ProgramXmlAgent.cs`
**Rol**: Genera program.xml y archivos de funciones de usuario (userFncFile)
**Name**: `"ProgramXml"`
**CanHandle**: `context.Plan?.RequiresProgramXml == true` (solo para CreateModule)

**Constructor**:
```csharp
ProgramXmlAgent(ILlmService _llmService, ILogger<ProgramXmlAgent> logger)
```

**ExecuteCoreAsync**:

1. **ExtractFunctionsFromGeneratedFiles()**: Parsea los archivos `.h` generados buscando declaraciones de funciones (lineas con parentesis que no son `#define` ni comentarios)

2. **ExtractEventsFromGeneratedFiles()**: Parsea archivos `.emic` generados buscando `EMIC:define(events.*)` para identificar eventos disponibles

3. **GenerateProgramXmlAsync()**: Usa LLM para generar `program.xml` con:
   - `<emic-program>` como root
   - `<emic-function-call>` para cada funcion del SDK
   - `<emic-function-parameter>` con tipo correcto (char → `emic-literal-char`, char* → `emic-literal-string`, uint → `emic-literal-numerical`)
   - `<emic-event-handler>` para eventos
   - Fallback: genera XML minimo si LLM falla

4. **Genera userFncFile.c / .h**: Stubs de funciones callback que el integrador puede implementar

5. **Agrega** 3 `GeneratedFile` (program.xml, userFncFile.c, userFncFile.h) a `context.GeneratedFiles`

---

### RuleValidatorAgent

**Archivo**: `Agents/RuleValidatorAgent.cs`
**Rol**: Ejecuta todos los validadores de reglas EMIC
**Name**: `"RuleValidator"`
**CanHandle**: `context.GeneratedFiles.Count > 0` (necesita archivos para validar)

**Constructor**:
```csharp
RuleValidatorAgent(IEnumerable<IValidator> _validators, ILogger<RuleValidatorAgent> logger)
```

**ExecuteCoreAsync**:

1. Itera todos los `IValidator` registrados en DI (5 en total)
2. Llama `validator.ValidateAsync(context, ct)` en cada uno
3. Agrega cada `ValidationResult` a `context.ValidationResults`
4. Si algun validador reporta errores (no solo warnings) → retorna Failure
5. Si solo hay warnings → retorna Success con mensaje de warnings

---

### MaterializerAgent

**Archivo**: `Agents/MaterializerAgent.cs`
**Rol**: Escribe archivos generados a disco y ejecuta TreeMaker para expandir
**Name**: `"Materializer"`
**CanHandle**: `context.GeneratedFiles.Count > 0`

**Constructor**:
```csharp
MaterializerAgent(MediaAccess _mediaAccess, IAgentSession _session, ILogger<MaterializerAgent> logger)
```

**ExecuteCoreAsync**:

1. **ExtractModulePath()**: Determina la ruta raiz del modulo desde los GeneratedFiles

2. **Escribe archivos**: Itera `context.GeneratedFiles` y escribe cada uno a disco via `_mediaAccess.File.WriteAllText()` usando virtual paths (`DEV:/{relativePath}`)

3. **TreeMaker.Generate()**: Ejecuta el generador EMIC sobre `generate.emic` para producir:
   - Archivos C expandidos en `Target/`
   - Archivos `.map` (TSV) en `System/map/TARGET/` para retropropagacion de errores

4. **SetContextPaths()**: Configura `context.Properties["ProjectPath"]` y `context.Properties["SystemPath"]` para que CompilationAgent sepa donde buscar

---

### CompilationAgent

**Archivo**: `Agents/CompilationAgent.cs`
**Rol**: Compila con XC16, parsea errores, retropropaga y aplica fixes simples
**Name**: `"Compilation"`
**CanHandle**: Verifica que `ProjectPath` existe en context.Properties

**Constructor**:
```csharp
CompilationAgent(
    ICompilationService _compilationService,
    CompilationErrorParser _errorParser,
    SourceMapper _sourceMapper,
    EmicAgentConfig _config,           // MaxCompilationRetries (default: 5)
    MediaAccess _mediaAccess,
    ILogger<CompilationAgent> logger)
```

**ExecuteCoreAsync**:

1. **Carga .map files**: Via `_sourceMapper.LoadMapFiles()` carga los archivos TSV generados por TreeMaker

2. **Loop de compilacion** (max `_config.MaxCompilationRetries` intentos):
   - Llama a `_compilationService.CompileAsync(projectPath, ct)`
   - Si exito → retorna Success
   - Si error → parsea output con `_errorParser.Parse()`
   - **TryBacktrackAndFix()**: Para cada error, usa `_sourceMapper.MapError()` para encontrar el archivo fuente original en el SDK
   - **TryApplySimpleFix()**: Intenta fixes automaticos (ej: agrega `#include` faltante si el error es "undefined reference" o "implicit declaration")
   - Reintenta compilacion

3. **Resultado**: `context.LastCompilation` con `Success`, `Errors`, `Warnings`, `AttemptNumber`

---

## Validadores Especializados

Todos implementan `IValidator` (`Services/Validation/IValidator.cs`):

```csharp
public interface IValidator
{
    string Name { get; }
    string Description { get; }
    Task<ValidationResult> ValidateAsync(AgentContext context, CancellationToken ct);
}
```

### LayerSeparationValidator

**Archivo**: `Agents/Validators/LayerSeparationValidator.cs`
**Name**: `"LayerSeparation"`
**Regla**: APIs SOLO deben usar funciones `HAL_*`, NUNCA acceso directo a registros de hardware

**Patrones regex que busca en archivos de tipo API (`_api/`)**:
- `HwRegisterRegex`: Detecta acceso directo a `TRIS*`, `LAT*`, `PORT*`, registros de timer (`T1CON`, `PR1`), SPI (`SPI*BUF`, `SPI*CON`), I2C (`I2C*CON`, `I2C*BRG`), interrupciones (`IFS*`, `IEC*`)
- `HalCallRegex`: Verifica uso de funciones `HAL_*`

**Severidad**: ERROR si encuentra acceso directo a registros en archivos de API

---

### NonBlockingValidator

**Archivo**: `Agents/Validators/NonBlockingValidator.cs`
**Name**: `"NonBlocking"`
**Regla**: No se permiten delays bloqueantes ni loops infinitos en APIs

**Patrones regex**:
- `DelayRegex`: `__delay_ms()`, `__delay_us()` → **ERROR** en cualquier lugar
- `InfiniteWhileRegex`: `while(1)`, `while(true)` → **ERROR** fuera de `main()`
- `InfiniteForRegex`: `for(;;)` → **ERROR** fuera de `main()`
- `WhileRegex`: Cualquier `while(...)` → **WARNING** si no tiene `break`, `return`, o check de timeout dentro de las siguientes 5 lineas

**Excepcion**: `main()` puede tener loop infinito (es el super-loop del embebido)

---

### StateMachineValidator

**Archivo**: `Agents/Validators/StateMachineValidator.cs`
**Name**: `"StateMachine"`
**Regla**: Funciones con timing deben usar patron de maquina de estados

**Patrones regex**:
- `FunctionSignatureRegex`: Extrae declaraciones de funcion
- `TimingRegex`: Detecta `getSystemMilis()`, `timer`, `poll`, `tick`
- `SwitchRegex`: Detecta sentencias `switch`
- `SkipFunctionRegex`: Excluye `main`, `*_init`, `init_*`

**Logica**:
- Funcion > 20 lineas con uso de timing pero sin `switch` → **WARNING** "Consider state machine pattern"
- `switch(state_var)` sin variable `static` para el estado → **WARNING**

---

### DependencyValidator

**Archivo**: `Agents/Validators/DependencyValidator.cs`
**Name**: `"Dependency"`
**Regla**: Todos los `EMIC:setInput(path)` deben apuntar a archivos existentes, sin dependencias circulares

**Logica**:
1. Construye conjuntos de paths: archivos generados + archivos del SDK
2. Parsea todas las lineas `EMIC:setInput(...)` de los archivos generados
3. Para cada referencia, verifica que el archivo destino existe → **ERROR** si no
4. Ejecuta DFS para deteccion de ciclos → **ERROR** si hay dependencia circular
5. Si no hay SDK state disponible → **WARNING** (no puede verificar completamente)

---

### BackwardsCompatibilityValidator

**Archivo**: `Agents/Validators/BackwardsCompatibilityValidator.cs`
**Name**: `"BackwardsCompatibility"`
**Regla**: Features opcionales deben estar protegidos con `EMIC:ifdef` / `#ifdef`

**Patrones regex**:
- `FunctionDefRegex`: Declaraciones y definiciones de funciones
- `EmicIfdefRegex`: Bloques `EMIC:ifdef` / `EMIC:endif`
- `CIfdefRegex`: Bloques `#ifdef` / `#endif`

**Funciones core excluidas** (no necesitan guards):
- `main`, `*_init`, `init_*`, `*_poll`, `poll_*`

**Logica**:
- Definiciones `events.*` y `polls.*` sin `EMIC:ifdef` → **WARNING**
- Declaraciones de funciones no-core fuera de `#ifdef` → **WARNING**
- Definiciones de funciones no-core fuera de `#ifdef` → **WARNING**

---

## Servicios

### LLM (Claude API)

#### ILlmService (`Services/Llm/ILlmService.cs`)

```csharp
public interface ILlmService
{
    Task<string> GenerateAsync(string prompt, CancellationToken ct);
    Task<string> GenerateWithContextAsync(string prompt, string context, CancellationToken ct);
    Task<T> GenerateStructuredAsync<T>(string prompt, CancellationToken ct);
}
```

#### ClaudeLlmService (`Cli/ClaudeLlmService.cs` — host-provided)

**Constructor**:
```csharp
ClaudeLlmService(EmicAgentConfig _config, HttpClient _httpClient, ILogger<ClaudeLlmService> _logger)
```

**Configuracion HTTP**:
- URL: `https://api.anthropic.com/v1/messages`
- Headers: `x-api-key`, `anthropic-version: 2023-06-01`, `content-type: application/json`
- Default model: `claude-sonnet-4-20250514` (configurable en `appsettings.json`)
- Default max_tokens: 4096, temperature: 0.2

**GenerateAsync(prompt)**: Envia prompt sin system context
**GenerateWithContextAsync(prompt, context)**: `context` se envia como `system` prompt, `prompt` como mensaje `user`
**GenerateStructuredAsync<T>()**: Llama GenerateAsync, limpia markdown fences, deserializa JSON a T

**Post-procesamiento**: `StripMarkdownFences()` elimina bloques ` ```json ... ``` ` de la respuesta

#### LlmPromptBuilder (`Services/Llm/LlmPromptBuilder.cs`)

Fluent builder para construir prompts:

```csharp
var prompt = new LlmPromptBuilder()
    .WithSystemInstruction("Eres un asistente EMIC...")
    .WithContext("SDK tiene 46 APIs...")      // Se concatena al system prompt
    .WithUserPrompt("Crea un modulo...")
    .Build();
// prompt.SystemPrompt = "Eres...\n\nCONTEXT:\nSDK tiene 46 APIs..."
// prompt.UserPrompt = "Crea un modulo..."
```

Retorna `record LlmPrompt(string SystemPrompt, string UserPrompt)`.

---

### SDK Scanner

#### ISdkScanner (`Services/Sdk/ISdkScanner.cs`)

```csharp
public interface ISdkScanner
{
    Task<SdkInventory> ScanAsync(string sdkPath, CancellationToken ct);
    Task<ApiDefinition?> FindApiAsync(string name, CancellationToken ct);
    Task<DriverDefinition?> FindDriverAsync(string name, CancellationToken ct);
    Task<ModuleDefinition?> FindModuleAsync(string name, CancellationToken ct);
}
```

#### SdkScanner (`Services/Sdk/SdkScanner.cs`)

**Constructor**:
```csharp
SdkScanner(MediaAccess _mediaAccess, IAgentSession _session, ILogger<SdkScanner> _logger)
```

**ScanAsync()**: Escanea las 4 carpetas principales del SDK:
- `EnumerateApis()` → `_api/{category}/{name}/*.emic` — extrae nombre, categoria, funciones, dependencias
- `EnumerateDrivers()` → `_drivers/{category}/{name}/*.emic` — extrae nombre, categoria, tipo de chip, HAL dependencies
- `EnumerateModules()` → `_modules/{category}/{name}/` — busca `System/generate.emic`, `deploy.emic`, lee `m_description.json`
- `EnumerateHal()` → `_hal/{name}/` — lista componentes HAL disponibles

**Cache**: El inventario se cachea internamente para evitar re-escaneos dentro del mismo scope.

#### SdkPathResolver (`Services/Sdk/SdkPathResolver.cs`)

Resuelve virtual paths EMIC a paths reales usando MediaAccess:
- `ResolvePath("DEV:_api/Sensors/LM35")` → path fisico

#### EmicFileParser (`Services/Sdk/EmicFileParser.cs`)

Parsea archivos `.emic` extrayendo:
- Directivas `EMIC:setInput()` (dependencias)
- Directivas `EMIC:define()` (inits, polls, c_modules, events, main_includes)
- Funciones publicadas (con tags EMIC-Codify)

---

### Templates

#### ITemplateEngine (`Services/Templates/ITemplateEngine.cs`)

```csharp
public interface ITemplateEngine
{
    string Render(string templateName, Dictionary<string, string> variables);
}
```

#### TemplateEngineService (`Services/Templates/TemplateEngineService.cs`)

Implementacion que resuelve templates por nombre y aplica variable substitution `{{variable}}`.

#### Templates disponibles

| Template | Archivo | Genera |
|----------|---------|--------|
| `ApiTemplate` | `Services/Templates/ApiTemplate.cs` | Estructura base para `.emic`, `.h`, `.c` de APIs |
| `DriverTemplate` | `Services/Templates/DriverTemplate.cs` | Estructura base para `.emic`, `.h`, `.c` de drivers |
| `ModuleTemplate` | `Services/Templates/ModuleTemplate.cs` | Estructura base para `generate.emic`, `deploy.emic`, `m_description.json` |

Cada template tiene metodos que retornan strings con placeholders `{{name}}`, `{{category}}`, `{{halDependency}}`, etc.

---

### Compilacion y SourceMapper

#### ICompilationService (`Services/Compilation/ICompilationService.cs`)

```csharp
public interface ICompilationService
{
    Task<CompilationResult> CompileAsync(string projectPath, CancellationToken ct);
}
```

#### EmicCompilationService (`Cli/EmicCompilationService.cs` — host-provided)

Ejecuta compilacion XC16 sobre el directorio del proyecto. Invoca el compilador nativo y captura stdout/stderr.

#### CompilationErrorParser (`Services/Compilation/CompilationErrorParser.cs`)

**Constructor**: `CompilationErrorParser(ILogger<CompilationErrorParser> _logger)`

**Parse(string compilerOutput)**: Parsea la salida del compilador XC16/GCC usando regex:
- **Formato GCC**: `file.c:42:10: error: undeclared identifier 'x'` → captura file, line, column, severity, code, message
- **Formato linker**: `xxx.o(.text+0x42): In function 'main': undefined reference to 'foo'`

Retorna `List<CompilationError>` con campos estructurados.

#### SourceMapper (`Services/Compilation/SourceMapper.cs`)

**Constructor**: `SourceMapper(ILogger<SourceMapper> _logger)`

**Proposito**: Mapea errores de compilacion del codigo expandido (en `Target/`) de vuelta a los archivos fuente originales del SDK.

**LoadMapFiles(MediaAccess, systemPath)**: Carga archivos `.map` desde `SYS:map/TARGET/`. Cada `.map` es un archivo TSV con formato:
```
originLine\toriginFile\tcomment
```
Donde cada linea del TSV corresponde a una linea del archivo expandido.

**MapError(error, generatedFiles, mapFiles)**: Estrategia en 2 pasos:
1. **TSV Maps (primario)**: Busca el archivo de error en los mapFiles. Si lo encuentra, indexa por numero de linea para obtener el archivo fuente original y la linea original.
2. **Fallback (secundario)**: Si no hay mapFiles, intenta matching por nombre de archivo contra los GeneratedFiles.

Retorna `SourceMappedError?` con el archivo fuente mapeado y la linea original.

---

### Metadata

#### IMetadataService (`Services/Metadata/IMetadataService.cs`)

```csharp
public interface IMetadataService
{
    Task<FolderMetadata?> ReadMetadataAsync(string folderPath, CancellationToken ct);
    Task WriteMetadataAsync(string folderPath, FolderMetadata metadata, CancellationToken ct);
    Task UpdateHistoryAsync(string folderPath, string action, string agentName, CancellationToken ct);
}
```

#### MetadataService (`Services/Metadata/MetadataService.cs`)

Lee/escribe archivos `.emic-meta.json` en cada carpeta del SDK para tracking de estado, relaciones y calidad.

---

### Validation Service

#### ValidationService (`Services/Validation/ValidationService.cs`)

**Constructor**: `ValidationService(IEnumerable<IValidator> _validators, ILogger<ValidationService> _logger)`

**ValidateAllAsync()**: Ejecuta todos los validadores registrados y agrega resultados. Usado por RuleValidatorAgent como helper.

---

## Modelos de Datos

### Enums del dominio (`Agents/Base/AgentContext.cs`)

```csharp
enum IntentType     { CreateModule, CreateApi, CreateDriver, ModifyExisting, QueryInfo, Unknown }
enum ProjectType    { Unknown, Monolithic, EmicModule, DistributedSystem }
enum ModuleRole     { Unknown, Sensor, Actuator, Display, Indicator, Communication, Other }
enum SystemKind     { Unknown, ClosedLoopControl, Monitoring, RemoteControl, Combined, Other }
enum ResultStatus   { Success, Failure, NeedsInput, Partial }
enum MessageType    { Request, Response, Error, Progress, Question }
enum FileType       { Emic, Header, Source, Json, Xml }
```

### DetailedSpecification (`Agents/Base/AgentContext.cs`)

Especificacion completa generada por la desambiguacion LLM-driven:

| Campo | Tipo | Descripcion |
|-------|------|-------------|
| `ProjectType` | `ProjectType` | Monolithic, EmicModule, DistributedSystem |
| `ModuleRole` | `ModuleRole` | Sensor, Actuator, Display, etc. |
| `SystemKind` | `SystemKind` | ClosedLoopControl, Monitoring, etc. |
| `Intent` | `IntentType` | CreateModule, CreateApi, CreateDriver |
| `ComponentName` | `string` | Nombre PascalCase del componente |
| `Category` | `string` | Categoria (Sensors, Communication, etc.) |
| `Description` | `string` | Descripcion en una linea |
| `SensorType` | `string` | Tipo de sensor (analogico, digital, etc.) |
| `CommunicationInterface` | `string` | Interfaz electrica del sensor (I2C, SPI, ADC) |
| `MeasurementRange` | `string` | Rango de medicion |
| `MeasurementUnit` | `string` | Unidad de medida |
| `Precision` | `string` | Precision requerida |
| `TargetPcb` | `string` | PCB destino |
| `ChipOrProtocol` | `string` | Chip o protocolo especifico |
| `OutputType` | `string` | Tipo de salida/comunicacion |
| `ReusableApis` | `List<string>` | APIs del SDK a reutilizar |
| `ReusableDrivers` | `List<string>` | Drivers del SDK a reutilizar |
| `ComponentsToCreate` | `List<string>` | Componentes nuevos necesarios |
| `RequiredDependencies` | `List<string>` | Dependencias requeridas |
| `AdditionalDetails` | `Dictionary<string, string>` | Campos extensibles |
| `ConversationHistory` | `List<DisambiguationExchange>` | Historial de Q&A |

### DisambiguationExchange (`Agents/Base/AgentContext.cs`)

```csharp
public class DisambiguationExchange
{
    string Question;          // Pregunta realizada
    string Answer;            // Respuesta del usuario
    string Reason;            // Razon de la pregunta (del LLM)
    List<string> Options;     // Opciones ofrecidas
}
```

### SdkInventory (`Models/Sdk/SdkInventory.cs`)

```csharp
public class SdkInventory
{
    string SdkRootPath;
    List<ApiDefinition> Apis;
    List<DriverDefinition> Drivers;
    List<ModuleDefinition> Modules;
    List<string> HalComponents;
    DateTime LastScanTime;
}
```

### ApiDefinition (`Models/Sdk/ApiDefinition.cs`)

| Campo | Tipo | Descripcion |
|-------|------|-------------|
| `Name` | `string` | Nombre de la API (ej: "LED", "Temperature") |
| `Category` | `string` | Categoria (ej: "LEDs", "Sensors") |
| `EmicFilePath` | `string` | Ruta al .emic principal |
| `IncPath` | `string` | Ruta a carpeta inc/ |
| `SrcPath` | `string` | Ruta a carpeta src/ |
| `Functions` | `List<string>` | Funciones publicadas |
| `Dependencies` | `List<string>` | Dependencias (drivers, HAL) |
| `Dictionaries` | `Dictionary<string, string>` | Registros EMIC (inits, polls, etc.) |

### DriverDefinition (`Models/Sdk/DriverDefinition.cs`)

| Campo | Tipo | Descripcion |
|-------|------|-------------|
| `Name` | `string` | Nombre del driver (ej: "LM35", "MCP2200") |
| `Category` | `string` | Categoria (ej: "Sensors", "Communication") |
| `EmicFilePath` | `string` | Ruta al .emic principal |
| `HalDependencies` | `List<string>` | HALs necesarios (GPIO, ADC, SPI) |
| `Functions` | `List<string>` | Funciones publicadas |
| `ChipType` | `string` | Tipo de chip que maneja |

### ModuleDefinition (`Models/Sdk/ModuleDefinition.cs`)

| Campo | Tipo | Descripcion |
|-------|------|-------------|
| `Name` | `string` | Nombre del modulo |
| `Category` | `string` | Categoria |
| `GenerateEmicPath` | `string` | Ruta a generate.emic |
| `DeployEmicPath` | `string` | Ruta a deploy.emic |
| `DescriptionJsonPath` | `string` | Ruta a m_description.json |
| `RequiredApis` | `List<string>` | APIs que usa |
| `RequiredDrivers` | `List<string>` | Drivers que usa |
| `HardwareBoard` | `string` | PCB target |

### GeneratedFile (`Models/Generation/GeneratedFile.cs`)

| Campo | Tipo | Descripcion |
|-------|------|-------------|
| `RelativePath` | `string` | Path relativo al SDK (ej: `_api/Sensors/Temp/Temp.emic`) |
| `Content` | `string` | Contenido del archivo |
| `Type` | `FileType` | Emic, Header, Source, Json, Xml |
| `GeneratedByAgent` | `string` | Nombre del agente que lo creo |
| `GeneratedAt` | `DateTime` | Timestamp de creacion |

### GenerationPlan (`Models/Generation/GenerationPlan.cs`)

| Campo | Tipo | Descripcion |
|-------|------|-------------|
| `ComponentName` | `string` | Nombre del componente a generar |
| `ComponentType` | `string` | "Api", "Driver", "Module" |
| `TargetPath` | `string` | Ruta destino en el SDK |
| `Description` | `string` | Descripcion del plan |
| `FilesToGenerate` | `List<PlannedFile>` | Archivos planificados (con Purpose) |
| `DependenciesToResolve` | `List<string>` | Dependencias a resolver |
| `RequiresProgramXml` | `bool` | Si true, ProgramXmlAgent se ejecuta |

### FolderMetadata (`Models/Metadata/FolderMetadata.cs`)

Representa `.emic-meta.json`:

```json
{
  "$schema": "emic-metadata-v1",
  "component": { "type": "api", "name": "Temperature", "path": "_api/Sensors/Temperature" },
  "relationships": {
    "dependsOn": [{"type": "hal", "path": "_hal/ADC/adc.emic"}],
    "usedBy": [{"type": "module", "path": "_modules/Sensors/TempSensor"}],
    "provides": {
      "functions": ["getTemperature", "temperature_init"],
      "dictionaries": {"inits": "temperature_init", "polls": "temperature_poll"}
    }
  },
  "quality": {
    "compilation": "pass",
    "ruleValidation": {"layerSeparation": "pass", "nonBlocking": "pass"},
    "debugState": "stable"
  },
  "history": [{"date": "2026-02-20", "action": "created", "agent": "ApiGeneratorAgent"}]
}
```

### ValidationResult / ValidationIssue (`Services/Validation/ValidationResult.cs`)

```csharp
public class ValidationResult
{
    string ValidatorName;
    bool Passed;
    List<ValidationIssue> Issues;
}

public class ValidationIssue
{
    string FilePath;           // Archivo con el issue
    int Line;                  // Linea del issue
    string Rule;               // Nombre de la regla violada
    string Message;            // Descripcion del issue
    ValidationSeverity Severity; // Warning o Error
}
```

### CompilationResult / CompilationError (`Agents/Base/AgentContext.cs`, `Services/Compilation/`)

```csharp
public class CompilationResult
{
    bool Success;
    List<string> Errors;
    List<string> Warnings;
    int AttemptNumber;
}

public class CompilationError
{
    string FilePath;
    int Line;
    int Column;
    string Severity;     // "error", "warning"
    string Code;         // Error code (ej: "E0001")
    string Message;      // Mensaje del compilador
}

public class SourceMappedError
{
    CompilationError OriginalError;
    GeneratedFile? MappedFile;     // Archivo fuente SDK
    int MappedLine;                // Linea en el fuente SDK
}
```

---

## Configuracion

### EmicAgentConfig (`Configuration/EmicAgentConfig.cs`)

| Propiedad | Tipo | Default | Descripcion |
|-----------|------|---------|-------------|
| `SdkPath` | `string` | (requerido) | Ruta al SDK EMIC |
| `MaxCompilationRetries` | `int` | 5 | Intentos maximos de compilacion |
| `DefaultMicrocontroller` | `string` | "pic24FJ64GA002" | MCU por defecto |
| `Language` | `string` | "es" | Idioma de interaccion |
| `Llm` | `LlmConfig` | — | Configuracion del LLM |

### LlmConfig (dentro de EmicAgentConfig)

| Propiedad | Tipo | Default | Descripcion |
|-----------|------|---------|-------------|
| `Provider` | `string` | "Claude" | Proveedor LLM |
| `Model` | `string` | "claude-sonnet-4-20250514" | Modelo especifico |
| `MaxTokens` | `int` | 4096 | Tokens maximos por respuesta |
| `Temperature` | `double` | 0.2 | Temperatura (baja = mas deterministico) |
| `ApiKey` | `string` | — | API key (o lee `ANTHROPIC_API_KEY` env var) |

**GetApiKey()**: Retorna `ApiKey` si existe, sino busca en variable de entorno `ANTHROPIC_API_KEY`. Si ninguno esta disponible, lanza excepcion.

### SdkPaths (`Configuration/SdkPaths.cs`)

Paths calculados del SDK:

| Propiedad | Valor |
|-----------|-------|
| `SdkRoot` | Config.SdkPath |
| `ApiRoot` | SdkRoot/_api |
| `DriversRoot` | SdkRoot/_drivers |
| `ModulesRoot` | SdkRoot/_modules |
| `HalRoot` | SdkRoot/_hal |
| `MainRoot` | SdkRoot/_main |

**Factory**: `SdkPaths.FromConfig(EmicAgentConfig config)` → crea instancia

### appsettings.json (CLI)

```json
{
  "EmicAgent": {
    "SdkPath": "C:\\path\\to\\PIC_XC16",
    "MaxCompilationRetries": 5,
    "DefaultMicrocontroller": "pic24FJ64GA002",
    "Language": "es"
  },
  "Llm": {
    "Provider": "Claude",
    "Model": "claude-sonnet-4-20250514",
    "MaxTokens": 4096,
    "Temperature": 0.2,
    "ApiKey": "sk-ant-..."
  }
}
```

---

## Implementacion CLI

### Program.cs (`Cli/Program.cs`)

Entry point de la aplicacion CLI:

1. Carga `appsettings.json` con `ConfigurationBuilder`
2. Bindea secciones a `EmicAgentConfig` y `LlmConfig`
3. Configura DI:
   - `services.AddEmicDevAgent(config)` — registra Core
   - `services.AddHttpClient<ClaudeLlmService>()` — HttpClient con factory
   - `services.AddScoped<ILlmService>(...)` → ClaudeLlmService
   - `services.AddSingleton<IUserInteraction, ConsoleUserInteraction>()`
   - `services.AddSingleton<IAgentSession, CliAgentSession>()`
   - `services.AddSingleton<IAgentEventSink, ConsoleEventSink>()`
   - `services.AddScoped<ICompilationService, EmicCompilationService>()`
4. Valida que `SdkPath` esta configurado
5. Lee prompt del usuario via `Console.ReadLine()`
6. Crea `AgentContext` con `DisambiguationOnly = true` (modo actual)
7. Ejecuta `OrchestratorAgent.ExecuteAsync(context)`
8. Muestra resultado: status, mensaje, spec JSON, archivos generados, issues de validacion

### CliAgentSession (`Cli/CliAgentSession.cs`)

Implementa `IAgentSession`:
- `UserEmail`: Desde config o variable de entorno
- `SdkPath`: Desde `EmicAgentConfig.SdkPath`
- `VirtualDrivers`: Mapa con `DEV` → SdkPath (para MediaAccess)

### ConsoleUserInteraction (`Cli/ConsoleUserInteraction.cs`)

Implementa `IUserInteraction`:
- **AskQuestionAsync()**: Imprime pregunta y opciones numeradas. Lee numero o texto libre. Soporta "Otro (especificar)" como ultima opcion
- **ReportProgressAsync()**: `Console.WriteLine($"[{agentName}] {message}")` con porcentaje opcional
- **ConfirmActionAsync()**: Pregunta si/no con `Console.ReadLine()`

### ConsoleEventSink (`Cli/ConsoleEventSink.cs`)

Implementa `IAgentEventSink`:
- Imprime eventos formateados a consola (step started/completed, file generated, validation, compilation)
- Todos los metodos retornan `Task.CompletedTask` despues de escribir

### NullAgentEventSink (`Configuration/NullAgentEventSink.cs`)

Fallback no-op: todos los metodos retornan `Task.CompletedTask` sin hacer nada. Se registra con `TryAddSingleton` — solo se usa si el host no registra su propio event sink.

---

## Estructura del Proyecto

```
EMIC_DevAgent/
├── EMIC_DevAgent.sln
├── docs/
│   ├── architecture.md                     # Este archivo
│   ├── EMIC_Conceptos_Clave.md            # Conceptos del SDK EMIC
│   ├── ESTADO_ACTUAL.md                   # Estado actual del proyecto
│   ├── PENDIENTES.md                      # Tareas pendientes
│   ├── WORKFLOW.md                        # Reglas de workflow del agente
│   └── MEJORAS_Y_SERVICIOS_COMPARTIDOS.md # Analisis historico
├── src/
│   ├── EMIC_DevAgent.Cli/                 # Punto de entrada CLI
│   │   ├── Program.cs                     # Entry point + DI setup
│   │   ├── CliAgentSession.cs             # IAgentSession → config + virtual drivers
│   │   ├── ConsoleUserInteraction.cs      # IUserInteraction → Console I/O
│   │   ├── ConsoleEventSink.cs            # IAgentEventSink → Console output
│   │   ├── ClaudeLlmService.cs            # ILlmService → HTTP Anthropic API
│   │   ├── EmicCompilationService.cs      # ICompilationService → XC16 compiler
│   │   ├── appsettings.json               # Config (SdkPath, LLM, etc.)
│   │   └── EMIC_DevAgent.Cli.csproj
│   └── EMIC_DevAgent.Core/               # Libreria Core (pura, sin host)
│       ├── Agents/
│       │   ├── Base/
│       │   │   ├── IAgent.cs              # Contrato de agente
│       │   │   ├── AgentBase.cs           # Clase abstracta con try/catch
│       │   │   ├── AgentContext.cs         # Contexto compartido + enums + DTOs
│       │   │   ├── AgentMessage.cs         # Mensajeria inter-agente
│       │   │   ├── AgentResult.cs          # Resultado estandarizado
│       │   │   ├── IUserInteraction.cs     # Abstraccion de interaccion
│       │   │   └── IAgentEventSink.cs      # Abstraccion de eventos
│       │   ├── Validators/
│       │   │   ├── LayerSeparationValidator.cs
│       │   │   ├── NonBlockingValidator.cs
│       │   │   ├── StateMachineValidator.cs
│       │   │   ├── DependencyValidator.cs
│       │   │   └── BackwardsCompatibilityValidator.cs
│       │   ├── OrchestratorAgent.cs        # Coordinador principal
│       │   ├── AnalyzerAgent.cs            # Scanner de SDK
│       │   ├── ApiGeneratorAgent.cs        # Generador de APIs
│       │   ├── DriverGeneratorAgent.cs     # Generador de drivers
│       │   ├── ModuleGeneratorAgent.cs     # Generador de modulos
│       │   ├── ProgramXmlAgent.cs          # Generador de program.xml
│       │   ├── CompilationAgent.cs         # Compilacion XC16
│       │   ├── MaterializerAgent.cs        # Escritura a disco
│       │   └── RuleValidatorAgent.cs       # Delegador de validacion
│       ├── Configuration/
│       │   ├── EmicAgentConfig.cs          # Config root + LlmConfig
│       │   ├── SdkPaths.cs                # Paths calculados del SDK
│       │   ├── IAgentSession.cs            # Abstraccion de sesion
│       │   ├── NullAgentEventSink.cs       # Fallback no-op
│       │   └── ServiceCollectionExtensions.cs  # AddEmicDevAgent()
│       ├── Models/
│       │   ├── Sdk/
│       │   │   ├── SdkInventory.cs         # Inventario completo
│       │   │   ├── ApiDefinition.cs        # Definicion de API
│       │   │   ├── DriverDefinition.cs     # Definicion de driver
│       │   │   └── ModuleDefinition.cs     # Definicion de modulo
│       │   ├── Metadata/
│       │   │   └── FolderMetadata.cs       # .emic-meta.json model
│       │   └── Generation/
│       │       ├── GeneratedFile.cs        # Archivo generado in-memory
│       │       └── GenerationPlan.cs       # Plan de generacion + PlannedFile
│       ├── Services/
│       │   ├── Llm/
│       │   │   ├── ILlmService.cs          # Contrato LLM
│       │   │   └── LlmPromptBuilder.cs     # Fluent builder de prompts
│       │   ├── Sdk/
│       │   │   ├── ISdkScanner.cs          # Contrato scanner
│       │   │   ├── SdkScanner.cs           # Implementacion de scan
│       │   │   ├── SdkPathResolver.cs      # Resolucion de virtual paths
│       │   │   └── EmicFileParser.cs       # Parser de archivos .emic
│       │   ├── Templates/
│       │   │   ├── ITemplateEngine.cs      # Contrato templates
│       │   │   ├── TemplateEngineService.cs # Implementacion
│       │   │   ├── ApiTemplate.cs          # Template para APIs
│       │   │   ├── DriverTemplate.cs       # Template para drivers
│       │   │   └── ModuleTemplate.cs       # Template para modulos
│       │   ├── Metadata/
│       │   │   ├── IMetadataService.cs     # Contrato metadata
│       │   │   └── MetadataService.cs      # Lee/escribe .emic-meta.json
│       │   ├── Compilation/
│       │   │   ├── ICompilationService.cs  # Contrato compilacion
│       │   │   ├── CompilationErrorParser.cs # Parser de errores XC16/GCC
│       │   │   └── SourceMapper.cs         # Retropropagacion via .map TSV
│       │   └── Validation/
│       │       ├── IValidator.cs           # Contrato validador
│       │       ├── ValidationResult.cs     # Resultado + issues
│       │       └── ValidationService.cs    # Ejecuta todos los validadores
│       └── EMIC_DevAgent.Core.csproj
└── tests/
    └── EMIC_DevAgent.Tests/               # Tests unitarios
        └── EMIC_DevAgent.Tests.csproj
```

---

## Flujo Completo de Ejecucion

### Ejemplo: "un modulo para medir temperatura"

```
1. CLI → OrchestratorAgent
   ├── ClassifyIntentAsync("un modulo para medir temperatura")
   │   └── LLM → intent=CreateModule, componentName=TemperatureSensor,
   │             category=Sensors, description="Modulo para medir temperatura"
   │
   ├── RunDisambiguationAsync()
   │   ├── FASE 0 (hardcoded):
   │   │   Q: "¿Que tipo de proyecto desea crear?"
   │   │   Options: Monolitico, Modulo EMIC, Sistema distribuido, Otro
   │   │   → User: "Modulo EMIC"  [implica I2C/EMIC-Bus]
   │   │
   │   ├── LLM iteration 1:
   │   │   Q: "¿Que tipo de modulo desea crear?"
   │   │   Options: Sensor, Actuador, Display, Indicador, Otro
   │   │   → User: "Sensor"
   │   │
   │   ├── LLM iteration 2:
   │   │   Q: "¿Que tipo de sensor de temperatura?"
   │   │   Options: Analogico (LM35, NTC), Digital (DS18B20, SHT30), Otro
   │   │   → User: "Analogico tipo LM35"
   │   │
   │   ├── LLM iteration 3:
   │   │   Q: "¿Cual es el rango de medicion requerido?"
   │   │   Options: -10 a 80°C, -40 a 125°C, 0 a 100°C, Otro
   │   │   → User: "-10 a 80 grados"
   │   │
   │   └── LLM iteration 4: → COMPLETE + JSON spec
   │
   ├── DisplaySpecificationAsync()
   │   === ESPECIFICACION FINAL ===
   │   Tipo de proyecto: Modulo EMIC (sistema modular, EMIC-Bus I2C)
   │   Rol: Sensor
   │   Componente: TemperatureSensor
   │   Intent: CreateModule
   │   Sensor: Analogico (LM35)
   │   Interfaz: ADC
   │   Rango: -10 a 80 °C
   │   ============================
   │
   └── [DisambiguationOnly = true] → STOP

   --- Flujo futuro (cuando DisambiguationOnly = false): ---

2. SDK Scan
   └── SdkScanner.ScanAsync() → 46 APIs, 12 drivers (inc. LM35), 49 modules

3. AnalyzerAgent
   ├── FindMatchingDrivers → LM35 encontrado
   ├── FindMatchingApis → Temperature API encontrada
   └── Gaps: Module TemperatureSensor (no existe)

4. DriverGeneratorAgent → [skip, LM35 ya existe]

5. ApiGeneratorAgent → [skip, Temperature API ya existe]

6. ModuleGeneratorAgent
   ├── generate.emic (includes PCB, Temperature API con driver=LM35)
   ├── deploy.emic (EMIC-TABS config)
   └── m_description.json (metadata)

7. ProgramXmlAgent
   ├── program.xml (calls getTemperature, event handlers)
   ├── userFncFile.c (stubs)
   └── userFncFile.h (declarations)

8. RuleValidatorAgent
   ├── LayerSeparation: PASS
   ├── NonBlocking: PASS
   ├── StateMachine: PASS
   ├── Dependency: PASS
   └── BackwardsCompatibility: PASS

9. MaterializerAgent
   ├── Escribe archivos a DEV:/_modules/Sensors/TemperatureSensor/
   └── TreeMaker.Generate() → Target/ + .map files

10. CompilationAgent
    ├── XC16 compile → Success
    └── context.LastCompilation = { Success=true }

11. REPORTE FINAL
    ├── 6 archivos generados
    ├── 0 issues de validacion
    └── Compilacion exitosa
```

### Patron de comunicacion entre agentes

Los agentes **NO se comunican directamente entre si**. Toda la comunicacion es via `AgentContext`:

```
Agent A ejecuta → lee de context → trabaja → escribe en context → retorna AgentResult
                                                                        ↓
OrchestratorAgent lee AgentResult → decide siguiente agente → lo ejecuta
                                                                        ↓
Agent B ejecuta → lee de context (incluye datos de A) → trabaja → escribe en context
```

Este patron mantiene los agentes **desacoplados** y permite reordenarlos, saltearlos, o agregar nuevos sin modificar los existentes.

---

## Resumen de Componentes

| Componente | Tipo | Archivo Principal | Rol | Dependencias Clave |
|------------|------|-------------------|-----|-------------------|
| OrchestratorAgent | Agent | `Agents/OrchestratorAgent.cs` | Coordinador principal | ILlmService, IUserInteraction, ISdkScanner, sub-agents |
| AnalyzerAgent | Agent | `Agents/AnalyzerAgent.cs` | Discovery de SDK | ISdkScanner |
| ApiGeneratorAgent | Agent | `Agents/ApiGeneratorAgent.cs` | Genera APIs | ILlmService, ITemplateEngine |
| DriverGeneratorAgent | Agent | `Agents/DriverGeneratorAgent.cs` | Genera drivers | ILlmService, ITemplateEngine |
| ModuleGeneratorAgent | Agent | `Agents/ModuleGeneratorAgent.cs` | Genera modulos | ILlmService, ITemplateEngine |
| ProgramXmlAgent | Agent | `Agents/ProgramXmlAgent.cs` | Genera program.xml | ILlmService |
| RuleValidatorAgent | Agent | `Agents/RuleValidatorAgent.cs` | Valida reglas | IEnumerable\<IValidator\> |
| MaterializerAgent | Agent | `Agents/MaterializerAgent.cs` | Escribe a disco | MediaAccess, IAgentSession |
| CompilationAgent | Agent | `Agents/CompilationAgent.cs` | Compila XC16 | ICompilationService, SourceMapper, CompilationErrorParser |
| LayerSeparationValidator | Validator | `Validators/LayerSeparationValidator.cs` | No registros directos en APIs | — |
| NonBlockingValidator | Validator | `Validators/NonBlockingValidator.cs` | No delays bloqueantes | — |
| StateMachineValidator | Validator | `Validators/StateMachineValidator.cs` | Patron state machine | — |
| DependencyValidator | Validator | `Validators/DependencyValidator.cs` | Paths validos, sin ciclos | — |
| BackwardsCompatibilityValidator | Validator | `Validators/BackwardsCompatibilityValidator.cs` | Guards ifdef | — |
| ClaudeLlmService | Service | `Cli/ClaudeLlmService.cs` | Claude HTTP API | HttpClient, EmicAgentConfig |
| SdkScanner | Service | `Services/Sdk/SdkScanner.cs` | Enumera SDK | MediaAccess, IAgentSession |
| EmicFileParser | Service | `Services/Sdk/EmicFileParser.cs` | Parsea .emic | — |
| CompilationErrorParser | Service | `Services/Compilation/CompilationErrorParser.cs` | Parsea errores XC16 | — |
| SourceMapper | Service | `Services/Compilation/SourceMapper.cs` | Retropropaga errores | — |
| MetadataService | Service | `Services/Metadata/MetadataService.cs` | .emic-meta.json | MediaAccess |
| ValidationService | Service | `Services/Validation/ValidationService.cs` | Runner de validadores | IEnumerable\<IValidator\> |
| TemplateEngineService | Service | `Services/Templates/TemplateEngineService.cs` | Renderiza templates | ApiTemplate, DriverTemplate, ModuleTemplate |
| LlmPromptBuilder | Service | `Services/Llm/LlmPromptBuilder.cs` | Builder de prompts | — |

---

*Ultima actualizacion: 2026-02-25*
*Validado contra codigo fuente commit actual en feature/devagent-implementation*
