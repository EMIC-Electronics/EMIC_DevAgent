# Capa _middleware — Especificacion de Implementacion (Discovery-Driven)

> Especificacion completa para la implementacion de la capa `_middleware` del SDK
> EMIC. Define bloques de procesamiento intermedios (filtros, detectores, colas,
> conversiones) que el **desarrollador** habilita en el modulo y el **integrador**
> selecciona, instancia y conecta desde el editor EMIC via el proceso Discovery.

---

## Indice

1. [Motivacion](#1-motivacion)
2. [Ubicacion en la Arquitectura de Capas](#2-ubicacion-en-la-arquitectura-de-capas)
3. [Estructura de Archivos](#3-estructura-de-archivos)
4. [Anatomia de un Componente Middleware](#4-anatomia-de-un-componente-middleware)
5. [Flujo de Trabajo: Las 4 Fases](#5-flujo-de-trabajo-las-4-fases)
6. [Metadata Discovery: EMIC:json(type = middleware)](#6-metadata-discovery-emicjsontype--middleware)
7. [Deteccion Automatica de Funciones Conectables](#7-deteccion-automatica-de-funciones-conectables)
8. [Ejemplos Completos](#8-ejemplos-completos)
9. [Reglas de la Capa _middleware](#9-reglas-de-la-capa-_middleware)
10. [Impacto en el DevAgent](#10-impacto-en-el-devagent)
11. [Glosario](#11-glosario)

---

## 1. Motivacion

### El problema

En la arquitectura actual del SDK EMIC, la logica de procesamiento intermedio
(filtrado, deteccion de umbrales, conversion de unidades, buffering) esta
embebida dentro de las APIs. Esto genera varios problemas:

1. **Duplicacion**: Si dos APIs necesitan el mismo filtro promedio movil,
   cada una reimplementa su propia version.

2. **Acoplamiento**: El filtro esta atado a la API que lo contiene. No se puede
   reutilizar el filtro de LoadCell en una API de temperatura diferente.

3. **Rigidez**: Cambiar el tipo de filtro requiere modificar el codigo fuente
   de la API. El integrador no puede elegir filtro sin tocar codigo interno.

4. **Composicion limitada**: No es posible encadenar procesadores
   (ej: filtro → detector de umbral → alarma) sin escribir codigo ad-hoc.

5. **Inaccesibilidad para el integrador**: El integrador no puede decidir
   que procesamiento aplicar; esa decision esta cableada en el codigo del
   desarrollador del SDK.

### La solucion

Una nueva capa `_middleware/` que contiene **bloques de procesamiento
independientes, parametrizables y conectables**. El mecanismo de conexion
se basa en el proceso **Discovery** del sistema EMIC:

- El **desarrollador del SDK** incluye los middleware disponibles en el modulo
  (sin parametros — solo declara disponibilidad)
- El **sistema EMIC (Discovery)** parsea cada middleware, extrae su metadata
  e identifica funciones de entrada/salida compatibles en las APIs del modulo
- El **integrador** selecciona, nombra, conecta y configura cada middleware
  desde el editor visual, como parte de su logica de aplicacion
- El **sistema EMIC (Generate)** produce el codigo C expandido con todas las
  conexiones resueltas en compile-time (zero overhead en runtime)

Cada bloque de middleware:

- Tiene una **entrada** (funcion de lectura, provista por un driver o API)
- Tiene una **salida** (funcion de escritura/evento, provista por una API)
- Es **parametrizable** (ventana de filtro, umbral, histeresis, etc.)
- Es **multi-instancia** (multiples filtros con diferentes configuraciones)
- Opera de forma **no-bloqueante** (poll-based, como toda la arquitectura EMIC)
- **NO accede a HAL ni hard** — solo consume funciones expuestas por otras capas
- Es **seleccionable por el integrador** desde el editor EMIC

---

## 2. Ubicacion en la Arquitectura de Capas

```
┌─────────────────────────────────────────────────────────┐
│  MODULO  (generate.emic + program.xml)                  │
│  Logica de negocio, configuracion, proyecto del usuario │
├─────────────────────────────────────────────────────────┤
│  API  (_api/)                                           │
│  Abstraccion funcional: funciones, variables, eventos   │
│  Registra inits y polls. Consume middleware y drivers.  │
├─────────────────────────────────────────────────────────┤
│  MIDDLEWARE  (_middleware/)        ◄── NUEVA CAPA       │
│  Bloques de procesamiento: filtros, detectores, colas,  │
│  conversiones. Conectables entre cualquier par de       │
│  funciones. Sin acceso a HAL/hard.                      │
│  Seleccionados por el integrador via Discovery.         │
├─────────────────────────────────────────────────────────┤
│  DRIVER  (_drivers/)                                    │
│  Control de hardware externo (chips, sensores, etc.)    │
│  Consume HAL para acceder a perifericos del MCU.        │
├─────────────────────────────────────────────────────────┤
│  HAL  (_hal/)                                           │
│  Abstraccion de perifericos internos del MCU            │
├─────────────────────────────────────────────────────────┤
│  HARD  (_hard/{mcuName}/)                               │
│  Codigo especifico del microcontrolador                 │
└─────────────────────────────────────────────────────────┘
```

### Relacion con capas existentes

```
                    ┌─────────┐
                    │ MODULO  │
                    └────┬────┘
                         │ configura y conecta
              ┌──────────┼──────────┐
              │          │          │
         ┌────▼────┐ ┌──▼───┐ ┌───▼────┐
         │   API   │ │ API  │ │  API   │
         └────┬────┘ └──┬───┘ └───┬────┘
              │         │         │
         ┌────▼─────────▼─────────▼────┐
         │        MIDDLEWARE           │
         │  ┌────────┐  ┌──────────┐  │
         │  │ Filtro  │  │ Detector │  │
         │  │ MA(8)   │→│ Umbral   │  │
         │  └────┬────┘  └────┬─────┘  │
         └───────┼────────────┼────────┘
                 │            │
         ┌───────▼────────────▼────────┐
         │          DRIVER             │
         │  LM35_readRaw()             │
         └─────────────────────────────┘
```

**Reglas de dependencia**:
- Middleware **puede consumir** funciones de drivers (como entrada)
- Middleware **puede invocar** funciones/eventos de APIs (como salida)
- Middleware **NO puede acceder** a HAL ni hard directamente
- Middleware **puede encadenarse** con otro middleware (salida → entrada)
- El **integrador decide** que middleware usar y como conectarlos (via Discovery)

### Roles y responsabilidades

| Rol | Responsabilidad en la capa middleware |
|-----|---------------------------------------|
| **Desarrollador del SDK** | Crea componentes middleware (`.emic` + `.h` + `.c`). Incluye los middleware utiles en el `generate.emic` de cada modulo, sin parametros. |
| **Sistema EMIC (Discovery)** | Parsea `EMIC:json(type = middleware)`, indexa componentes disponibles, clasifica funciones I/O compatibles de las APIs del modulo. |
| **Integrador** | Selecciona middleware del sidebar, los instancia con nombre, elige funciones de entrada/salida, configura parametros. Usa las funciones/eventos del middleware en `program.xml`. |
| **Sistema EMIC (Generate)** | Genera `EMIC:setInput` con todos los parametros resueltos, expande templates `.h`/`.c` con sustitucion de macros. Resultado: codigo C compilable con zero overhead. |

---

## 3. Estructura de Archivos

```
_middleware/
├── Filters/
│   ├── MovingAverage/
│   │   ├── MovingAverage.emic         # Orquestador: metadata + generacion condicional
│   │   ├── inc/
│   │   │   └── MovingAverage.h        # Template: interfaz con Discovery tags
│   │   └── src/
│   │       └── MovingAverage.c        # Template: implementacion
│   ├── IIR_LowPass/
│   │   ├── IIR_LowPass.emic
│   │   ├── inc/
│   │   │   └── IIR_LowPass.h
│   │   └── src/
│   │       └── IIR_LowPass.c
│   └── Median/
│       └── ...
├── Detectors/
│   ├── ThresholdDetector/
│   │   ├── ThresholdDetector.emic
│   │   ├── inc/
│   │   │   └── ThresholdDetector.h
│   │   └── src/
│   │       └── ThresholdDetector.c
│   ├── ZeroCrossing/
│   │   └── ...
│   └── PeakDetector/
│       └── ...
├── Queues/
│   ├── FIFO/
│   │   └── ...
│   └── CircularBuffer/
│       └── ...
├── Converters/
│   ├── LinearScale/
│   │   └── ...
│   ├── LookupTable/
│   │   └── ...
│   └── UnitConverter/
│       └── ...
└── Control/
    ├── PID/
    │   └── ...
    ├── Hysteresis/
    │   └── ...
    └── RateLimiter/
        └── ...
```

Cada componente sigue la misma estructura que APIs y drivers:
- `.emic` — orquestador: contiene metadata Discovery (`EMIC:json`) + generacion
  condicional de archivos (solo cuando el integrador lo instancia)
- `inc/*.h` — template de interfaz: funciones publicas con Discovery tags,
  protegidas por `EMIC:ifdef usedFunction.*` / `EMIC:ifdef usedEvent.*`
- `src/*.c` — template de implementacion: logica de procesamiento con
  placeholders `.{name}.`, `.{inputFn}.`, `.{outputFn}.`, etc.

---

## 4. Anatomia de un Componente Middleware

### Modelo generico

Un componente middleware es un **bloque de procesamiento con nombre** que tiene:

```
         ┌───────────────────────────────┐
         │  Middleware: .{name}.          │
         │                               │
  input ─┤  int32_t → [proceso] → int32_t├─ output
         │                               │
         │  Parametros: threshold,       │
         │  windowSize, alpha, etc.      │
         └───────────────────────────────┘
```

- **name**: Identificador unico de la instancia, asignado por el integrador
  desde el editor (ej: `TempAlarm`, `PressureFilter`)
- **input**: Funcion que provee el dato crudo — seleccionada por el integrador
  de una lista de funciones compatibles (ej: `LM35_readRaw`, `getTemperature`)
- **output**: Funcion que recibe el dato procesado — seleccionada por el integrador
  de una lista de funciones/eventos compatibles (ej: `eOverTemperature`)
- **parametros**: Constantes de configuracion con valores por defecto editables
  por el integrador. Algunos son modificables en runtime via `program.xml`
- **poll**: Funcion no-bloqueante que ejecuta el ciclo input → proceso → output

### Diferencia clave con APIs

| Aspecto | API | Middleware |
|---------|-----|-----------|
| Registra init/poll | Si, en main loop | Si, automaticamente al instanciarse |
| Accede a HAL/hard | Via drivers | Nunca |
| Expone funciones al integrador | Si (Discovery) | Si, al instanciarse (Discovery) |
| Quien la configura | El desarrollador (generate.emic) | El integrador (editor visual) |
| Instanciacion | Una vez por API | Multiples instancias con nombre unico |
| I/O | Define sus propias funciones | Conecta funciones de otras capas |
| Visible en Discovery | Siempre | Solo cuando el desarrollador la habilita |

### Estructura de los 3 archivos

#### 1. Archivo `.emic` — Orquestador

El `.emic` tiene dos secciones claramente separadas:

```
┌─────────────────────────────────────────────────────────────┐
│  SECCION 1: METADATA DISCOVERY (siempre se ejecuta)         │
│                                                              │
│  EMIC:json(type = middleware) { ... }                        │
│  → Parseado por Discovery para generar la lista del sidebar  │
│  → Define parametros, tipos I/O, funciones que expone        │
│                                                              │
├──────────────────────────────────────────────────────────────┤
│  SECCION 2: GENERACION DE CODIGO (solo si name= existe)     │
│                                                              │
│  EMIC:ifdef name                                             │
│    EMIC:copy(inc/... > TARGET:inc/..., ...)                  │
│    EMIC:copy(src/... > TARGET:..., ...)                      │
│    EMIC:define(main_includes.X, X)                           │
│    EMIC:define(c_modules.X, X)                               │
│    EMIC:define(inits.X, X_init)                              │
│    EMIC:define(polls.X, X_poll)                              │
│  EMIC:endif                                                  │
│                                                              │
│  → Solo se ejecuta cuando el sistema genera con parametros   │
│  → El integrador nunca ve esta seccion                       │
└──────────────────────────────────────────────────────────────┘
```

La **seccion 1** se ejecuta siempre que el archivo es incluido por `EMIC:setInput`
(incluso sin parametros). Discovery la parsea para indexar el componente.

La **seccion 2** solo se ejecuta cuando el sistema provee el parametro `name=`
(despues de que el integrador instancia el middleware). Sin `name=`, el
`EMIC:ifdef name` evalua falso y no se genera ningun archivo.

#### 2. Archivo `.h` — Template de interfaz

```
┌─────────────────────────────────────────────────────────────┐
│  Header guards con .{name}.                                  │
│  Forward declarations: extern inputFn / outputFn             │
│  Funciones init/poll (siempre presentes)                     │
│  Funciones publicas con Discovery tags (@fn, @alias, @brief) │
│    → Protegidas por EMIC:ifdef usedFunction.X                │
│  Variables publicas con Discovery tags (@var)                 │
│  Eventos propios con EMIC:ifdef usedEvent.X                  │
└──────────────────────────────────────────────────────────────┘
```

#### 3. Archivo `.c` — Template de implementacion

```
┌─────────────────────────────────────────────────────────────┐
│  #include del .h correspondiente                             │
│  Variables estaticas con prefijo .{name}. (multi-instancia)  │
│  Funcion _init(): inicializa estado interno                  │
│  Funcion _poll(): lee inputFn → procesa → escribe outputFn   │
│  lastOutput_.{name}.: almacena ultimo valor para getOutput   │
│  Funciones de reconfiguracion runtime (set*, get*)           │
│    → Protegidas por EMIC:ifdef usedFunction.X                │
└──────────────────────────────────────────────────────────────┘
```

### Mecanismo de doble salida

Cada middleware tiene **dos mecanismos de salida** que coexisten:

1. **outputFn** (conexion directa): La funcion de salida seleccionada por el
   integrador al instanciar. Se llama cada vez que el middleware detecta la
   condicion o produce un resultado. Se resuelve en compile-time.

2. **Eventos propios** (ej: `eThresholdCrossed_{name}`): Evento EMIC estandar
   protegido por `EMIC:ifdef usedEvent.*`. El integrador puede implementar
   handlers en `program.xml` para logica adicional.

```c
// En el poll del middleware — ambos mecanismos:
void ThresholdDetector_.{name}._poll(void) {
    .{dataType}. value = .{inputFn}.();

    if (!active_.{name}.) {
        if (value >= threshold_.{name}.) {
            active_.{name}. = 1;

            // Mecanismo 1: outputFn (conexion directa)
            .{outputFn}.(value);

            // Mecanismo 2: evento propio (si el integrador lo usa)
            EMIC:ifdef usedEvent.eThresholdCrossed_.{name}.
            eThresholdCrossed_.{name}.(value);
            EMIC:endif
        }
    } else {
        if (value < (threshold_.{name}. - hysteresis_.{name}.)) {
            active_.{name}. = 0;
        }
    }
}
```

Esto permite que el integrador:
- Use el `outputFn` para conexion directa con otra API o middleware (automatico)
- Use el evento propio para logica adicional en `program.xml` (opcional)
- O ambos simultaneamente

### Encadenamiento entre middlewares

Cada middleware expone una funcion `{name}_getOutput()` que retorna el ultimo
valor procesado. Esto permite que otro middleware la use como `inputFn`:

```c
// En cada middleware .c:
static .{dataType}. lastOutput_.{name}. = 0;

// Funcion publica para encadenamiento (pull)
.{dataType}. .{name}._getOutput(void) {
    return lastOutput_.{name}.;
}

// En poll: despues de procesar, almacenar Y llamar outputFn
void MovingAverage_.{name}._poll(void) {
    .{dataType}. raw = .{inputFn}.();
    // ... procesamiento ...
    lastOutput_.{name}. = filtered;
    .{outputFn}.(filtered);  // push al siguiente
}
```

Cuando el integrador encadena dos middlewares, el sistema conecta
`{name1}_getOutput` como `inputFn` del segundo middleware. El orden de
ejecucion de los polls determina el flujo de datos.

---

## 5. Flujo de Trabajo: Las 4 Fases

El mecanismo de conexion de middleware se divide en 4 fases secuenciales,
cada una con un actor diferente:

```
┌─────────────────────────────────────────────────────────────────────┐
│  FASE 1: DESARROLLO (por el desarrollador del SDK)                 │
│                                                                     │
│  generate.emic:                                                     │
│    EMIC:setInput(DEV:_api/Sensors/Temperature/Temperature.emic,    │
│                  driver=LM35)                                       │
│    EMIC:setInput(DEV:_middleware/Filters/MovingAverage/...)  ←(*)   │
│    EMIC:setInput(DEV:_middleware/Detectors/ThresholdDetector/...) ← │
│    EMIC:setInput(DEV:_middleware/Converters/LinearScale/...)  ←     │
│                                                                     │
│  (*) Sin parametros — solo habilita disponibilidad                  │
└─────────────────────────────────────┬───────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────┐
│  FASE 2: DISCOVERY (automatico por el sistema EMIC)                │
│                                                                     │
│  1. Parsea cada middleware incluido → extrae EMIC:json metadata     │
│  2. Parsea cada API incluida → identifica funciones I/O compatibles │
│  3. Genera lista de middleware disponibles en el sidebar             │
│  4. Genera lista de funciones "conectables" (entradas y salidas)    │
└─────────────────────────────────────┬───────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────┐
│  FASE 3: INTEGRACION (por el integrador en el editor EMIC)         │
│                                                                     │
│  Sidebar muestra:                                                   │
│   ┌─ Middleware Disponibles ──────────────────────────┐             │
│   │  ▸ MovingAverage — Filtro promedio movil           │             │
│   │  ▸ ThresholdDetector — Detector de umbral          │             │
│   │  ▸ LinearScale — Conversion lineal                 │             │
│   └───────────────────────────────────────────────────┘             │
│                                                                     │
│  El integrador selecciona "ThresholdDetector" del sidebar.          │
│  El sistema solicita:                                               │
│    - Nombre de instancia: "TempAlarm"                               │
│    - Entrada: [getTemperature ▼] (lista de funciones compatibles)   │
│    - Salida:  [eAlarm ▼]         (lista de eventos compatibles)     │
│    - threshold: 80  (editable, valor por defecto del middleware)     │
│    - hysteresis: 5  (editable)                                      │
│                                                                     │
│  El sistema infiere dataType=int32_t del prototipo de               │
│  getTemperature() → int32_t getTemperature(void)                    │
│                                                                     │
│  Al confirmar, el sistema agrega al sidebar:                        │
│   ┌─ TempAlarm (ThresholdDetector) ───────────────────┐             │
│   │  ▸ fn: setThreshold_TempAlarm(int32_t)            │             │
│   │  ▸ fn: setHysteresis_TempAlarm(int32_t)           │             │
│   │  ▸ fn: getState_TempAlarm() → uint8_t             │             │
│   │  ▸ var: threshold_TempAlarm (int32_t)             │             │
│   │  ▸ ev: eThresholdCrossed_TempAlarm(int32_t)       │             │
│   └───────────────────────────────────────────────────┘             │
└─────────────────────────────────────┬───────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────┐
│  FASE 4: GENERATE (automatico por el sistema EMIC)                 │
│                                                                     │
│  El sistema genera la invocacion con todos los parametros:          │
│    EMIC:setInput(DEV:_middleware/.../ThresholdDetector.emic,        │
│                  name=TempAlarm, inputFn=getTemperature,            │
│                  outputFn=eAlarm, threshold=80, hysteresis=5,      │
│                  dataType=int32_t)                                  │
│                                                                     │
│  El .emic detecta que name= existe → genera archivos .h y .c       │
│  expandidos con sustitucion de macros → codigo C compilable.        │
└─────────────────────────────────────────────────────────────────────┘
```

### 5.1. Fase 1: Desarrollo

El desarrollador del modulo incluye los middleware que considera utiles para
el integrador, **sin parametros** (solo registra su disponibilidad):

**generate.emic** (escrito por el desarrollador):
```
// Hardware
EMIC:setInput(DEV:_pcb/pcb.emic, pcb=HRD_TEMP_SENSOR_V1)

// APIs (con sus drivers)
EMIC:setInput(DEV:_api/Sensors/Temperature/Temperature.emic, driver=LM35)
EMIC:setInput(DEV:_api/Timers/timer_api.emic, name=1)
EMIC:setInput(DEV:_api/Indicators/LEDs/led.emic, name=Alarm, pin=Led1)
EMIC:setInput(DEV:_api/Wired_Communication/EMICBus/EMICBus.emic, port=2, frameID=0)

// Middleware disponibles para el integrador (sin parametros)
EMIC:setInput(DEV:_middleware/Filters/MovingAverage/MovingAverage.emic)
EMIC:setInput(DEV:_middleware/Filters/Median/Median.emic)
EMIC:setInput(DEV:_middleware/Detectors/ThresholdDetector/ThresholdDetector.emic)
EMIC:setInput(DEV:_middleware/Converters/LinearScale/LinearScale.emic)

// System
EMIC:setInput(SYS:usedFunction.emic)
EMIC:setInput(SYS:usedEvent.emic)
EMIC:setInput(DEV:_main/main.emic)
```

**Efecto**: Al no pasar parametros, cada `.emic` de middleware solo ejecuta
su seccion de metadata (`EMIC:json`). La seccion de generacion (`EMIC:ifdef name`)
no se activa. No se genera ningun archivo `.h` ni `.c` en esta fase.

### 5.2. Fase 2: Discovery

El proceso Discovery, al encontrar un bloque `EMIC:json(type = middleware)`,
extrae la metadata y la agrega al inventario del modulo. Ademas, escanea
todas las APIs incluidas para identificar **funciones conectables**.

El resultado de Discovery es:

1. **Lista de middleware disponibles**: Nombre, categoria, descripcion,
   parametros configurables, tipos de I/O aceptados, funciones que expone
   al instanciarse.

2. **Lista de funciones de entrada compatibles**: Funciones getter de APIs
   y drivers (`tipo nombreFn(void)`) clasificadas por tipo de retorno.

3. **Lista de funciones/eventos de salida compatibles**: Funciones y eventos
   de APIs (`void nombreFn(tipo valor)`) clasificadas por tipo de parametro.

### 5.3. Fase 3: Integracion

El sidebar del editor EMIC muestra una seccion **"Middleware Disponibles"**
y una seccion **"Middleware Instanciados"**:

```
┌─ Recursos del Modulo ──────────────────────────────────┐
│                                                         │
│  ▾ Funciones                                            │
│    ▸ getTemperature() → int32_t                         │
│    ▸ setTime1(uint32_t, char)                           │
│    ▸ setLed_Alarm(uint8_t)                              │
│                                                         │
│  ▾ Eventos                                              │
│    ▸ eTemperatureReady(int32_t)                          │
│    ▸ etOut1()                                            │
│    ▸ eUSB(char*, streamIn_t*)                            │
│                                                         │
│  ▾ Variables                                            │
│    ▸ Capacidad (float)                                  │
│                                                         │
│  ▾ Middleware Disponibles                               │
│    ▸ MovingAverage — Filtro promedio movil               │
│    ▸ Median — Filtro de mediana                          │
│    ▸ ThresholdDetector — Detector de umbral              │
│    ▸ LinearScale — Conversion lineal                     │
│                                                         │
│  ▾ Middleware Instanciados                              │
│    (vacio — el integrador aun no ha instanciado ninguno)│
│                                                         │
└─────────────────────────────────────────────────────────┘
```

El integrador hace clic en un middleware disponible y el sistema muestra un
**dialogo de instanciacion**:

```
┌─ Instanciar: ThresholdDetector ─────────────────────────┐
│                                                          │
│  Detector de umbral con histeresis. Genera un evento     │
│  cuando el valor de entrada cruza el umbral configurado. │
│                                                          │
│  Nombre de instancia: [TempAlarm_________]               │
│                                                          │
│  Entrada (funcion que provee datos):                     │
│  ┌──────────────────────────────────────────┐            │
│  │ ▸ getTemperature() → int32_t        [✓]  │            │
│  │   getTemperatureRaw() → int32_t     [ ]  │            │
│  │   getWeight() → int32_t             [ ]  │            │
│  │   getWeightKg() → float             [ ]  │            │
│  └──────────────────────────────────────────┘            │
│                                                          │
│  Salida (funcion/evento que recibe el resultado):        │
│  ┌──────────────────────────────────────────┐            │
│  │ ▸ eOverTemperature(int32_t)         [✓]  │            │
│  │   eTemperatureReady(int32_t)        [ ]  │            │
│  │   sendValue(int32_t)                [ ]  │            │
│  │   setLed_Alarm(uint8_t)             [⚠]  │  ← tipo   │
│  └──────────────────────────────────────────┘  diferente │
│                                                          │
│  Tipo de dato: int32_t (autodetectado de getTemperature) │
│                                                          │
│  Parametros:                                             │
│    threshold:  [80________] (modificable en runtime)     │
│    hysteresis: [5_________] (modificable en runtime)     │
│                                                          │
│  [Cancelar]                              [Instanciar]    │
└──────────────────────────────────────────────────────────┘
```

**Al presionar Instanciar**, el sistema:

1. Registra la instancia: nombre=`TempAlarm`, input=`getTemperature`,
   output=`eOverTemperature`, dataType=`int32_t`, threshold=80, hysteresis=5.

2. Agrega al sidebar las funciones, variables y eventos de la instancia:

```
│  ▾ Middleware Instanciados                               │
│    ▾ TempAlarm (ThresholdDetector)                       │
│      ▸ fn: setThreshold_TempAlarm(int32_t)               │
│      ▸ fn: setHysteresis_TempAlarm(int32_t)              │
│      ▸ fn: getState_TempAlarm() → uint8_t                │
│      ▸ var: threshold_TempAlarm (int32_t)                │
│      ▸ ev: eThresholdCrossed_TempAlarm(int32_t)          │
```

3. Estas funciones quedan disponibles para arrastrar a `program.xml`:

```xml
<!-- program.xml — el integrador usa las funciones del middleware -->
<emic-event name="eThresholdCrossed_TempAlarm">
    <!-- Encender LED de alarma cuando se cruza el umbral -->
    <emic-function name="setLed_Alarm">
        <emic-function-parameter type="uint8_t">
            <emic-literal-numerical value="1"/>
        </emic-function-parameter>
    </emic-function>
</emic-event>

<emic-event name="etOut1">
    <!-- Cada segundo, actualizar el threshold dinamicamente -->
    <emic-function name="setThreshold_TempAlarm">
        <emic-function-parameter type="int32_t">
            <emic-literal-numerical value="85"/>
        </emic-function-parameter>
    </emic-function>
</emic-event>
```

### 5.4. Fase 4: Generate

Cuando el sistema ejecuta EMIC:Generate, procesa las instancias de middleware
registradas por el integrador. Para cada instancia, genera una invocacion
`EMIC:setInput` con todos los parametros resueltos:

**Invocacion generada automaticamente** (no la escribe el usuario):
```
EMIC:setInput(DEV:_middleware/Detectors/ThresholdDetector/ThresholdDetector.emic,
              name=TempAlarm,
              inputFn=getTemperature,
              outputFn=eOverTemperature,
              threshold=80,
              hysteresis=5,
              dataType=int32_t)
```

El `.emic` del middleware detecta que `name=` esta definido, entra en la
seccion `EMIC:ifdef name`, y ejecuta los `EMIC:copy` que generan los archivos
`.h` y `.c` expandidos con sustitucion de macros.

**Resultado**: Codigo C compilable con todas las funciones resueltas en
compile-time, sin punteros a funcion, sin overhead de runtime.

---

## 6. Metadata Discovery: EMIC:json(type = middleware)

Cada middleware declara su metadata con un bloque `EMIC:json(type = middleware)`.
Este bloque es parseado por el proceso Discovery para generar la lista del sidebar
y el dialogo de instanciacion.

### Estructura completa del JSON

```javascript
EMIC:json(type = middleware)
{
    // ──────────────────────────────────────────────
    // IDENTIFICACION
    // ──────────────────────────────────────────────
    "name": "ThresholdDetector",          // Nombre del componente
    "category": "Detectors",              // Categoria (subcarpeta en _middleware/)
    "brief": "Detector de umbral con histeresis",
    "description": "Genera un evento cuando el valor de entrada cruza el umbral
                    configurado. Incluye histeresis para evitar rebotes en
                    señales ruidosas.",

    // ──────────────────────────────────────────────
    // PARAMETROS CONFIGURABLES
    // ──────────────────────────────────────────────
    "parameters": [
        {
            "name": "threshold",          // Nombre del parametro (placeholder .{threshold}.)
            "type": "int32_t",            // Tipo C del parametro
            "default": "100",             // Valor por defecto mostrado en el dialogo
            "brief": "Valor de umbral para la deteccion",
            "runtime": true               // true = modificable en runtime via set*()
                                          // false = solo compile-time (fijo en el .c)
        },
        {
            "name": "hysteresis",
            "type": "int32_t",
            "default": "10",
            "brief": "Banda muerta para evitar rebotes",
            "runtime": true
        }
    ],

    // ──────────────────────────────────────────────
    // ESPECIFICACION DE ENTRADA
    // ──────────────────────────────────────────────
    "input": {
        "type": "numeric",                // Categoria general del tipo
        "accepts": [                      // Tipos C aceptados como entrada
            "int16_t", "int32_t",
            "uint16_t", "uint32_t",
            "float"
        ],
        "brief": "Señal a monitorear"     // Descripcion para el dialogo
    },

    // ──────────────────────────────────────────────
    // ESPECIFICACION DE SALIDA
    // ──────────────────────────────────────────────
    "output": {
        "type": "numeric",
        "produces": "same_as_input",      // Tipo de salida = tipo de entrada
                                          // Alternativa: tipo fijo como "uint8_t"
        "brief": "Valor que cruzo el umbral",
        "mode": "event"                   // "event" = solo invoca al detectar condicion
                                          // "continuous" = invoca cada poll
    },

    // ──────────────────────────────────────────────
    // FUNCIONES/VARIABLES/EVENTOS QUE EXPONE
    // ──────────────────────────────────────────────
    "provides": {
        "functions": [
            {
                "name": "setThreshold_{name}",
                "signature": "void setThreshold_{name}({dataType} value)",
                "brief": "Modifica el umbral en runtime",
                "runtime": true           // Disponible para program.xml
            },
            {
                "name": "setHysteresis_{name}",
                "signature": "void setHysteresis_{name}({dataType} value)",
                "brief": "Modifica la histeresis en runtime",
                "runtime": true
            },
            {
                "name": "getState_{name}",
                "signature": "uint8_t getState_{name}(void)",
                "brief": "Retorna 1 si el umbral esta activo, 0 si no"
            },
            {
                "name": "getOutput_{name}",
                "signature": "{dataType} getOutput_{name}(void)",
                "brief": "Retorna el ultimo valor procesado (para encadenamiento)"
            }
        ],
        "variables": [
            {
                "name": "threshold_{name}",
                "type": "{dataType}",
                "brief": "Valor actual del umbral de deteccion"
            }
        ],
        "events": [
            {
                "name": "eThresholdCrossed_{name}",
                "signature": "extern void eThresholdCrossed_{name}({dataType} value)",
                "brief": "Se dispara cuando el valor cruza el umbral"
            }
        ]
    }
}
```

### Campos del JSON

| Campo | Tipo | Requerido | Descripcion |
|-------|------|:---------:|-------------|
| `name` | string | Si | Nombre del componente middleware |
| `category` | string | Si | Subcarpeta en `_middleware/` |
| `brief` | string | Si | Descripcion corta (una linea) para el sidebar |
| `description` | string | Si | Descripcion larga para el dialogo de instanciacion |
| `parameters` | array | Si | Lista de parametros configurables |
| `parameters[].name` | string | Si | Nombre del parametro (usado como placeholder `.{name}.`) |
| `parameters[].type` | string | Si | Tipo C del parametro |
| `parameters[].default` | string | Si | Valor por defecto |
| `parameters[].brief` | string | Si | Descripcion del parametro |
| `parameters[].runtime` | bool | No | `true` si es modificable en runtime (default: `false`) |
| `parameters[].options` | array | No | Lista de valores permitidos (ej: `["4","8","16","32"]`) |
| `input` | object | Si | Especificacion de la entrada del middleware |
| `input.type` | string | Si | Categoria: `"numeric"`, `"boolean"`, `"buffer"` |
| `input.accepts` | array | Si | Tipos C aceptados como entrada |
| `input.brief` | string | Si | Descripcion de la entrada |
| `output` | object | Si | Especificacion de la salida del middleware |
| `output.type` | string | Si | Categoria del tipo de salida |
| `output.produces` | string | Si | Tipo de salida: `"same_as_input"` o tipo fijo |
| `output.brief` | string | Si | Descripcion de la salida |
| `output.mode` | string | No | `"event"` o `"continuous"` (default: `"continuous"`) |
| `provides` | object | Si | Funciones, variables y eventos que la instancia expone |
| `provides.functions` | array | Si | Lista de funciones publicas |
| `provides.variables` | array | No | Lista de variables publicas |
| `provides.events` | array | No | Lista de eventos propios |

### Resolucion de placeholders en el JSON

Los placeholders `{name}` y `{dataType}` en el JSON se resuelven en el momento
de la instanciacion (Fase 3), no durante Discovery. Discovery los interpreta
como plantillas:

| Placeholder | Momento de resolucion | Ejemplo |
|-------------|----------------------|---------|
| `{name}` | Fase 3 (integrador asigna nombre) | `setThreshold_{name}` → `setThreshold_TempAlarm` |
| `{dataType}` | Fase 3 (autodetectado de inputFn) | `{dataType} value` → `int32_t value` |

---

## 7. Deteccion Automatica de Funciones Conectables

El proceso Discovery clasifica las funciones de cada API incluida en el modulo
en dos categorias: funciones aptas como **entrada** y funciones aptas como
**salida** de middleware.

### Funciones de ENTRADA potenciales (aptas como `inputFn`)

**Criterios de deteccion**:
- Firma: `{tipo_numerico} nombreFuncion(void)` — retorna valor, sin parametros
- Tag Discovery: `@fn` con retorno numerico y cero parametros
- Se excluyen: funciones `void`, funciones con parametros, funciones de init/poll

**Ejemplo de escaneo**:
```
Escaneando APIs del modulo...

Encontradas funciones compatibles como ENTRADA de middleware:
  [Temperature API]
    int32_t getTemperature(void)        — @alias Temperature
    int32_t getTemperatureRaw(void)     — @alias RawADC
  [LoadCell API]
    int32_t getWeight(void)             — @alias Weight
    float   getWeightKg(void)           — @alias WeightKg
  [ADC Driver]
    uint16_t ADC_readChannel0(void)     — @alias ADC_CH0
```

### Funciones/eventos de SALIDA potenciales (aptas como `outputFn`)

**Criterios de deteccion**:
- Firma: `void nombreFuncion({tipo_numerico} valor)` — un parametro numerico
- Firma alternativa: `extern void evento({tipo_numerico} valor)` — eventos EMIC
- Tag Discovery: `@fn` o `@fn extern` con un parametro numerico
- Se excluyen: funciones con multiples parametros (excepto eventos EMIC),
  funciones getter, funciones de init/poll

**Ejemplo de escaneo**:
```
Encontradas funciones compatibles como SALIDA de middleware:
  [Temperature API]
    extern void eTemperatureReady(int32_t value)  — @alias TempReady
    extern void eOverTemperature(int32_t value)   — @alias OverTemp
  [LED API]
    void setLed_Alarm(uint8_t state)              — @alias AlarmLed
  [EMICBus API]
    void sendValue(int32_t value)                 — @alias SendBus
```

### Verificacion de compatibilidad de tipos

Cuando el integrador selecciona una funcion de entrada, el sistema filtra
las funciones de salida por compatibilidad de tipos:

```
inputFn retorna int32_t
    → outputFn acepta int32_t? → COMPATIBLE (sin indicador)
    → outputFn acepta float?   → WARNING: posible perdida de precision
    → outputFn acepta uint8_t? → WARNING: posible truncamiento
    → outputFn acepta char*?   → ERROR: tipos incompatibles (no se muestra)
```

En el dialogo de instanciacion:
- Las funciones con tipo exacto se muestran primero, sin indicador
- Las funciones con tipo compatible pero diferente se muestran con `[⚠]`
- Las funciones con tipo incompatible se ocultan

### Inferencia automatica de `dataType`

El `dataType` del middleware se infiere automaticamente del prototipo de la
funcion de entrada seleccionada:

```
El integrador selecciona: getTemperature() → int32_t getTemperature(void)
                                               ├───────┘
dataType = int32_t     ◄───────────────────────┘
```

El integrador no necesita seleccionar el tipo de dato manualmente. El sistema
lo muestra como informacion confirmativa: `Tipo de dato: int32_t (autodetectado)`.

### Funciones de middleware instanciado como entrada/salida

Una vez que un middleware ha sido instanciado, sus funciones de salida
(`getOutput_{name}`) y eventos (`eThresholdCrossed_{name}`) se agregan
a las listas de funciones conectables. Esto permite **encadenamiento**:

```
Despues de instanciar TempFilter (MovingAverage):

Funciones de ENTRADA actualizadas:
  ...funciones de API existentes...
  int32_t TempFilter_getOutput(void)    — @alias TempFilter.Output
                                          [Middleware: MovingAverage]

→ El integrador puede usar TempFilter_getOutput como inputFn de otro middleware
```

---

## 8. Ejemplos Completos

### 8.1. ThresholdDetector — Detector de Umbral

**Caso de uso**: Generar un evento cuando la temperatura supera 80°C, con
histeresis de 5°C para evitar rebotes.

#### ThresholdDetector.emic

```
// ================================================================
// SECCION 1: METADATA DISCOVERY
// ================================================================
EMIC:json(type = middleware)
{
    "name": "ThresholdDetector",
    "category": "Detectors",
    "brief": "Detector de umbral con histeresis",
    "description": "Genera un evento cuando el valor de entrada cruza el umbral
                    configurado. Incluye histeresis para evitar rebotes.",
    "parameters": [
        {
            "name": "threshold",
            "type": "int32_t",
            "default": "100",
            "brief": "Valor de umbral para la deteccion",
            "runtime": true
        },
        {
            "name": "hysteresis",
            "type": "int32_t",
            "default": "10",
            "brief": "Banda muerta para evitar rebotes",
            "runtime": true
        }
    ],
    "input": {
        "type": "numeric",
        "accepts": ["int16_t", "int32_t", "uint16_t", "uint32_t", "float"],
        "brief": "Señal a monitorear"
    },
    "output": {
        "type": "numeric",
        "produces": "same_as_input",
        "brief": "Valor que cruzo el umbral",
        "mode": "event"
    },
    "provides": {
        "functions": [
            {
                "name": "setThreshold_{name}",
                "signature": "void setThreshold_{name}({dataType} value)",
                "brief": "Modifica el umbral en runtime",
                "runtime": true
            },
            {
                "name": "setHysteresis_{name}",
                "signature": "void setHysteresis_{name}({dataType} value)",
                "brief": "Modifica la histeresis en runtime",
                "runtime": true
            },
            {
                "name": "getState_{name}",
                "signature": "uint8_t getState_{name}(void)",
                "brief": "Retorna 1 si el umbral esta activo, 0 si no"
            },
            {
                "name": "getOutput_{name}",
                "signature": "{dataType} getOutput_{name}(void)",
                "brief": "Retorna el ultimo valor que cruzo el umbral"
            }
        ],
        "variables": [
            {
                "name": "threshold_{name}",
                "type": "{dataType}",
                "brief": "Valor actual del umbral"
            }
        ],
        "events": [
            {
                "name": "eThresholdCrossed_{name}",
                "signature": "extern void eThresholdCrossed_{name}({dataType} value)",
                "brief": "Se dispara cuando el valor cruza el umbral"
            }
        ]
    }
}

// ================================================================
// SECCION 2: GENERACION DE CODIGO (solo si name= fue provisto)
// ================================================================
EMIC:ifdef name
    EMIC:copy(inc/ThresholdDetector.h > TARGET:inc/ThresholdDetector_.{name}..h,
              name=.{name}.,
              inputFn=.{inputFn}.,
              outputFn=.{outputFn}.,
              threshold=.{threshold}.,
              hysteresis=.{hysteresis}.,
              dataType=.{dataType}.)

    EMIC:copy(src/ThresholdDetector.c > TARGET:ThresholdDetector_.{name}..c,
              name=.{name}.,
              inputFn=.{inputFn}.,
              outputFn=.{outputFn}.,
              threshold=.{threshold}.,
              hysteresis=.{hysteresis}.,
              dataType=.{dataType}.)

    EMIC:define(main_includes.ThresholdDetector_.{name}.,ThresholdDetector_.{name}.)
    EMIC:define(c_modules.ThresholdDetector_.{name}.,ThresholdDetector_.{name}.)
    EMIC:define(inits.ThresholdDetector_.{name}.,ThresholdDetector_.{name}._init)
    EMIC:define(polls.ThresholdDetector_.{name}.,ThresholdDetector_.{name}._poll)
EMIC:endif
```

#### ThresholdDetector.h (template)

```c
#ifndef _THRESHOLD_DETECTOR_.{name}._H_
#define _THRESHOLD_DETECTOR_.{name}._H_

#include <stdint.h>

// --- Funciones de I/O (resueltas por parametros en Fase 4) ---
extern .{dataType}. .{inputFn}.(void);

EMIC:ifdef usedEvent.eThresholdCrossed_.{name}.
extern void eThresholdCrossed_.{name}.(.{dataType}. value);
EMIC:endif

// --- Init / Poll ---
void ThresholdDetector_.{name}._init(void);
void ThresholdDetector_.{name}._poll(void);

// --- Funciones expuestas al integrador ---

/**
* @fn void setThreshold_.{name}.(.{dataType}. value);
* @alias SetThreshold_.{name}.
* @brief Modifica el umbral de deteccion en runtime
* @param value Nuevo valor de umbral
*/
EMIC:ifdef usedFunction.setThreshold_.{name}.
void setThreshold_.{name}.(.{dataType}. value);
EMIC:endif

/**
* @fn void setHysteresis_.{name}.(.{dataType}. value);
* @alias SetHysteresis_.{name}.
* @brief Modifica la histeresis en runtime
* @param value Nuevo valor de histeresis
*/
EMIC:ifdef usedFunction.setHysteresis_.{name}.
void setHysteresis_.{name}.(.{dataType}. value);
EMIC:endif

/**
* @fn uint8_t getState_.{name}.(void);
* @alias GetState_.{name}.
* @brief Retorna 1 si el umbral esta activo, 0 si no
* @return Estado del detector (0 o 1)
*/
EMIC:ifdef usedFunction.getState_.{name}.
uint8_t getState_.{name}.(void);
EMIC:endif

/**
* @fn .{dataType}. getOutput_.{name}.(void);
* @alias GetOutput_.{name}.
* @brief Retorna el ultimo valor que cruzo el umbral (para encadenamiento)
* @return Ultimo valor procesado
*/
.{dataType}. getOutput_.{name}.(void);

/**
* @var .{dataType}. threshold_.{name}.;
* @alias Threshold_.{name}.
* @brief Valor actual del umbral de deteccion
*/
extern .{dataType}. threshold_.{name}.;

#endif
```

#### ThresholdDetector.c (template)

```c
#include "inc/ThresholdDetector_.{name}..h"

static .{dataType}. threshold_.{name}. = .{threshold}.;
static .{dataType}. hysteresis_.{name}. = .{hysteresis}.;
static uint8_t active_.{name}. = 0;
static .{dataType}. lastOutput_.{name}. = 0;

void ThresholdDetector_.{name}._init(void) {
    active_.{name}. = 0;
    lastOutput_.{name}. = 0;
}

void ThresholdDetector_.{name}._poll(void) {
    // 1. Leer entrada (funcion seleccionada por el integrador)
    .{dataType}. value = .{inputFn}.();

    // 2. Detectar cruce de umbral con histeresis
    if (!active_.{name}.) {
        if (value >= threshold_.{name}.) {
            active_.{name}. = 1;
            lastOutput_.{name}. = value;

            // Salida directa (funcion seleccionada por el integrador)
            .{outputFn}.(value);

            // Evento propio (si el integrador lo usa en program.xml)
            EMIC:ifdef usedEvent.eThresholdCrossed_.{name}.
            eThresholdCrossed_.{name}.(value);
            EMIC:endif
        }
    } else {
        if (value < (threshold_.{name}. - hysteresis_.{name}.)) {
            active_.{name}. = 0;
        }
    }
}

.{dataType}. getOutput_.{name}.(void) {
    return lastOutput_.{name}.;
}

EMIC:ifdef usedFunction.setThreshold_.{name}.
void setThreshold_.{name}.(.{dataType}. newThreshold) {
    threshold_.{name}. = newThreshold;
}
EMIC:endif

EMIC:ifdef usedFunction.setHysteresis_.{name}.
void setHysteresis_.{name}.(.{dataType}. newHysteresis) {
    hysteresis_.{name}. = newHysteresis;
}
EMIC:endif

EMIC:ifdef usedFunction.getState_.{name}.
uint8_t getState_.{name}.(void) {
    return active_.{name}.;
}
EMIC:endif
```

#### Resultado expandido

Despues de que el integrador instancia con nombre=`TempAlarm`,
inputFn=`getTemperature`, outputFn=`eOverTemperature`, threshold=80,
hysteresis=5, dataType=`int32_t`, el sistema genera:

**ThresholdDetector_TempAlarm.h** (generado):
```c
#ifndef _THRESHOLD_DETECTOR_TempAlarm_H_
#define _THRESHOLD_DETECTOR_TempAlarm_H_

#include <stdint.h>

extern int32_t getTemperature(void);
extern void eThresholdCrossed_TempAlarm(int32_t value);  // si el integrador lo usa

void ThresholdDetector_TempAlarm_init(void);
void ThresholdDetector_TempAlarm_poll(void);

void setThreshold_TempAlarm(int32_t value);     // si el integrador lo usa
void setHysteresis_TempAlarm(int32_t value);    // si el integrador lo usa
uint8_t getState_TempAlarm(void);               // si el integrador lo usa
int32_t getOutput_TempAlarm(void);
extern int32_t threshold_TempAlarm;

#endif
```

**ThresholdDetector_TempAlarm.c** (generado):
```c
#include "inc/ThresholdDetector_TempAlarm.h"

static int32_t threshold_TempAlarm = 80;
static int32_t hysteresis_TempAlarm = 5;
static uint8_t active_TempAlarm = 0;
static int32_t lastOutput_TempAlarm = 0;

void ThresholdDetector_TempAlarm_init(void) {
    active_TempAlarm = 0;
    lastOutput_TempAlarm = 0;
}

void ThresholdDetector_TempAlarm_poll(void) {
    int32_t value = getTemperature();

    if (!active_TempAlarm) {
        if (value >= threshold_TempAlarm) {
            active_TempAlarm = 1;
            lastOutput_TempAlarm = value;
            eOverTemperature(value);
            eThresholdCrossed_TempAlarm(value);
        }
    } else {
        if (value < (threshold_TempAlarm - hysteresis_TempAlarm)) {
            active_TempAlarm = 0;
        }
    }
}

int32_t getOutput_TempAlarm(void) { return lastOutput_TempAlarm; }
void setThreshold_TempAlarm(int32_t newThreshold) { threshold_TempAlarm = newThreshold; }
void setHysteresis_TempAlarm(int32_t newHysteresis) { hysteresis_TempAlarm = newHysteresis; }
uint8_t getState_TempAlarm(void) { return active_TempAlarm; }
```

**Flujo de datos**:
```
getTemperature() ──► ThresholdDetector(80,5) ──► eOverTemperature()
    [API fn]              [middleware]              [API event]
                               │
                               └──► eThresholdCrossed_TempAlarm()
                                        [evento propio → program.xml]
```

---

### 8.2. MovingAverage — Filtro Promedio Movil

**Caso de uso**: Suavizar la lectura ruidosa de un sensor de temperatura LM35
antes de que la API la procese.

#### MovingAverage.emic

```
// ================================================================
// SECCION 1: METADATA DISCOVERY
// ================================================================
EMIC:json(type = middleware)
{
    "name": "MovingAverage",
    "category": "Filters",
    "brief": "Filtro promedio movil de ventana fija",
    "description": "Suaviza una señal calculando el promedio de las ultimas N
                    muestras. Reduce ruido manteniendo la tendencia general.",
    "parameters": [
        {
            "name": "windowSize",
            "type": "uint8_t",
            "default": "8",
            "brief": "Cantidad de muestras en la ventana",
            "options": ["4", "8", "16", "32", "64"],
            "runtime": false
        }
    ],
    "input": {
        "type": "numeric",
        "accepts": ["int16_t", "int32_t", "uint16_t", "uint32_t"],
        "brief": "Señal a filtrar (lectura cruda del sensor)"
    },
    "output": {
        "type": "numeric",
        "produces": "same_as_input",
        "brief": "Señal filtrada (promedio movil)",
        "mode": "continuous"
    },
    "provides": {
        "functions": [
            {
                "name": "getOutput_{name}",
                "signature": "{dataType} getOutput_{name}(void)",
                "brief": "Retorna el ultimo valor filtrado"
            },
            {
                "name": "reset_{name}",
                "signature": "void reset_{name}(void)",
                "brief": "Reinicia el buffer del filtro",
                "runtime": true
            }
        ],
        "variables": [
            {
                "name": "filterCount_{name}",
                "type": "uint8_t",
                "brief": "Cantidad de muestras acumuladas"
            }
        ]
    }
}

// ================================================================
// SECCION 2: GENERACION DE CODIGO
// ================================================================
EMIC:ifdef name
    EMIC:copy(inc/MovingAverage.h > TARGET:inc/MovingAverage_.{name}..h,
              name=.{name}., inputFn=.{inputFn}., outputFn=.{outputFn}.,
              windowSize=.{windowSize}., dataType=.{dataType}.)

    EMIC:copy(src/MovingAverage.c > TARGET:MovingAverage_.{name}..c,
              name=.{name}., inputFn=.{inputFn}., outputFn=.{outputFn}.,
              windowSize=.{windowSize}., dataType=.{dataType}.)

    EMIC:define(main_includes.MovingAverage_.{name}.,MovingAverage_.{name}.)
    EMIC:define(c_modules.MovingAverage_.{name}.,MovingAverage_.{name}.)
    EMIC:define(inits.MovingAverage_.{name}.,MovingAverage_.{name}._init)
    EMIC:define(polls.MovingAverage_.{name}.,MovingAverage_.{name}._poll)
EMIC:endif
```

#### MovingAverage.h (template)

```c
#ifndef _MOVING_AVERAGE_.{name}._H_
#define _MOVING_AVERAGE_.{name}._H_

#include <stdint.h>

// Funciones de I/O (resueltas por parametros)
extern .{dataType}. .{inputFn}.(void);
extern void .{outputFn}.(.{dataType}. value);

// Init / Poll
void MovingAverage_.{name}._init(void);
void MovingAverage_.{name}._poll(void);

/**
* @fn .{dataType}. getOutput_.{name}.(void);
* @alias GetOutput_.{name}.
* @brief Retorna el ultimo valor filtrado
* @return Valor promediado
*/
.{dataType}. getOutput_.{name}.(void);

/**
* @fn void reset_.{name}.(void);
* @alias Reset_.{name}.
* @brief Reinicia el buffer del filtro a cero
*/
EMIC:ifdef usedFunction.reset_.{name}.
void reset_.{name}.(void);
EMIC:endif

/**
* @var uint8_t filterCount_.{name}.;
* @alias FilterCount_.{name}.
* @brief Cantidad de muestras acumuladas en el buffer
*/
extern uint8_t filterCount_.{name}.;

#endif
```

#### MovingAverage.c (template)

```c
#include "inc/MovingAverage_.{name}..h"

#define WINDOW_SIZE_.{name}. .{windowSize}.

static .{dataType}. buffer_.{name}.[WINDOW_SIZE_.{name}.];
static uint8_t index_.{name}. = 0;
static .{dataType}. accumulator_.{name}. = 0;
uint8_t filterCount_.{name}. = 0;
static .{dataType}. lastOutput_.{name}. = 0;

void MovingAverage_.{name}._init(void) {
    uint8_t i;
    for (i = 0; i < WINDOW_SIZE_.{name}.; i++) {
        buffer_.{name}.[i] = 0;
    }
    index_.{name}. = 0;
    accumulator_.{name}. = 0;
    filterCount_.{name}. = 0;
    lastOutput_.{name}. = 0;
}

void MovingAverage_.{name}._poll(void) {
    // 1. Leer entrada
    .{dataType}. raw = .{inputFn}.();

    // 2. Procesar (filtro promedio movil)
    accumulator_.{name}. -= buffer_.{name}.[index_.{name}.];
    buffer_.{name}.[index_.{name}.] = raw;
    accumulator_.{name}. += raw;
    index_.{name}.++;
    if (index_.{name}. >= WINDOW_SIZE_.{name}.) index_.{name}. = 0;
    if (filterCount_.{name}. < WINDOW_SIZE_.{name}.) filterCount_.{name}.++;

    .{dataType}. filtered = accumulator_.{name}. / filterCount_.{name}.;
    lastOutput_.{name}. = filtered;

    // 3. Escribir salida
    .{outputFn}.(filtered);
}

.{dataType}. getOutput_.{name}.(void) {
    return lastOutput_.{name}.;
}

EMIC:ifdef usedFunction.reset_.{name}.
void reset_.{name}.(void) {
    MovingAverage_.{name}._init();
}
EMIC:endif
```

#### Flujo completo desde el punto de vista del integrador

1. El desarrollador incluyo `MovingAverage.emic` en generate.emic (sin parametros)
2. Discovery lo indexa → aparece en sidebar como "MovingAverage — Filtro promedio movil"
3. El integrador lo selecciona y configura:
   - Nombre: `TempFilter`
   - Entrada: `LM35_readRaw() → int32_t` (del driver)
   - Salida: `Temperature_onFiltered(int32_t)` (de la API)
   - windowSize: `16`
4. El sistema genera el codigo expandido:

```
LM35_readRaw() ──► MovingAverage(16) ──► Temperature_onFiltered()
    [driver]          [middleware]              [API callback]
```

5. En el sidebar aparecen: `getOutput_TempFilter()`, `reset_TempFilter()`,
   `filterCount_TempFilter`
6. El integrador puede usar `reset_TempFilter()` en program.xml

---

### 8.3. LinearScale — Conversor de Unidades

**Caso de uso**: Convertir lectura de ADC cruda (0-4095) a temperatura en
centesimas de grado (-5000 a +15000 = -50.00°C a +150.00°C).

#### LinearScale.emic

```
EMIC:json(type = middleware)
{
    "name": "LinearScale",
    "category": "Converters",
    "brief": "Conversion lineal (escala + offset)",
    "description": "Convierte un valor de entrada aplicando la formula:
                    output = (input * factor / divisor) + offset.
                    Util para calibracion de sensores y conversion de unidades.",
    "parameters": [
        {
            "name": "factor",
            "type": "int32_t",
            "default": "1000",
            "brief": "Factor multiplicador (numerador)",
            "runtime": true
        },
        {
            "name": "divisor",
            "type": "int32_t",
            "default": "1000",
            "brief": "Divisor (denominador)",
            "runtime": false
        },
        {
            "name": "offset",
            "type": "int32_t",
            "default": "0",
            "brief": "Offset sumado al resultado",
            "runtime": true
        }
    ],
    "input": {
        "type": "numeric",
        "accepts": ["int16_t", "int32_t", "uint16_t", "uint32_t"],
        "brief": "Valor crudo a convertir"
    },
    "output": {
        "type": "numeric",
        "produces": "same_as_input",
        "brief": "Valor convertido (escalado + offset)",
        "mode": "continuous"
    },
    "provides": {
        "functions": [
            {
                "name": "getOutput_{name}",
                "signature": "{dataType} getOutput_{name}(void)",
                "brief": "Retorna el ultimo valor convertido"
            },
            {
                "name": "setFactor_{name}",
                "signature": "void setFactor_{name}({dataType} value)",
                "brief": "Modifica el factor en runtime",
                "runtime": true
            },
            {
                "name": "setOffset_{name}",
                "signature": "void setOffset_{name}({dataType} value)",
                "brief": "Modifica el offset en runtime",
                "runtime": true
            }
        ]
    }
}

EMIC:ifdef name
    EMIC:copy(inc/LinearScale.h > TARGET:inc/LinearScale_.{name}..h,
              name=.{name}., inputFn=.{inputFn}., outputFn=.{outputFn}.,
              factor=.{factor}., divisor=.{divisor}., offset=.{offset}.,
              dataType=.{dataType}.)

    EMIC:copy(src/LinearScale.c > TARGET:LinearScale_.{name}..c,
              name=.{name}., inputFn=.{inputFn}., outputFn=.{outputFn}.,
              factor=.{factor}., divisor=.{divisor}., offset=.{offset}.,
              dataType=.{dataType}.)

    EMIC:define(main_includes.LinearScale_.{name}.,LinearScale_.{name}.)
    EMIC:define(c_modules.LinearScale_.{name}.,LinearScale_.{name}.)
    EMIC:define(inits.LinearScale_.{name}.,LinearScale_.{name}._init)
    EMIC:define(polls.LinearScale_.{name}.,LinearScale_.{name}._poll)
EMIC:endif
```

#### LinearScale.c (template)

```c
#include "inc/LinearScale_.{name}..h"

static .{dataType}. factor_.{name}. = .{factor}.;
static .{dataType}. offset_.{name}. = .{offset}.;
static .{dataType}. lastOutput_.{name}. = 0;

void LinearScale_.{name}._init(void) {
    lastOutput_.{name}. = 0;
}

void LinearScale_.{name}._poll(void) {
    .{dataType}. raw = .{inputFn}.();
    .{dataType}. scaled = (raw * factor_.{name}.) / .{divisor}. + offset_.{name}.;
    lastOutput_.{name}. = scaled;
    .{outputFn}.(scaled);
}

.{dataType}. getOutput_.{name}.(void) {
    return lastOutput_.{name}.;
}

EMIC:ifdef usedFunction.setFactor_.{name}.
void setFactor_.{name}.(.{dataType}. newFactor) {
    factor_.{name}. = newFactor;
}
EMIC:endif

EMIC:ifdef usedFunction.setOffset_.{name}.
void setOffset_.{name}.(.{dataType}. newOffset) {
    offset_.{name}. = newOffset;
}
EMIC:endif
```

**Flujo de datos** (instancia: name=ADCtoTemp, factor=3663, divisor=1000, offset=-5000):
```
LM35_readRaw() ──► LinearScale(×3.663, -50.00) ──► Temperature_onCalibrated()
    [driver]              [middleware]                       [API]
    ADC: 0-4095      centesimas: -5000 a +15000         grados ×100
```

---

### 8.4. Cadena de Middlewares — Filtro + Conversion + Detector

**Caso de uso**: Para un sensor de temperatura: filtrar ruido, convertir unidades,
y detectar umbral. Todo configurado por el integrador desde el editor.

#### Paso a paso del integrador

El desarrollador incluyo en generate.emic:
```
EMIC:setInput(DEV:_api/Sensors/Temperature/Temperature.emic, driver=LM35)
EMIC:setInput(DEV:_middleware/Filters/MovingAverage/MovingAverage.emic)
EMIC:setInput(DEV:_middleware/Converters/LinearScale/LinearScale.emic)
EMIC:setInput(DEV:_middleware/Detectors/ThresholdDetector/ThresholdDetector.emic)
```

**El integrador instancia 3 middlewares en cadena**:

**Instancia 1** — Filtro de ruido:
- Middleware: MovingAverage
- Nombre: `Pipe_filter`
- Entrada: `LM35_readRaw()` (driver)
- Salida: `Temperature_onFiltered()` (API callback)
- windowSize: 8

**Instancia 2** — Conversion de unidades:
- Middleware: LinearScale
- Nombre: `Pipe_scale`
- Entrada: `Pipe_filter_getOutput()` ← salida del middleware anterior
- Salida: `Temperature_onCalibrated()` (API callback)
- factor: 3663, divisor: 1000, offset: -5000

**Instancia 3** — Detector de alarma:
- Middleware: ThresholdDetector
- Nombre: `Pipe_alarm`
- Entrada: `Pipe_scale_getOutput()` ← salida del middleware anterior
- Salida: `eOverTemperature()` (API event)
- threshold: 8000, hysteresis: 200

#### Invocaciones generadas por el sistema (Fase 4)

```
EMIC:setInput(DEV:_middleware/Filters/MovingAverage/MovingAverage.emic,
              name=Pipe_filter, inputFn=LM35_readRaw,
              outputFn=Temperature_onFiltered, windowSize=8, dataType=int32_t)

EMIC:setInput(DEV:_middleware/Converters/LinearScale/LinearScale.emic,
              name=Pipe_scale, inputFn=Pipe_filter_getOutput,
              outputFn=Temperature_onCalibrated, factor=3663, offset=-5000,
              divisor=1000, dataType=int32_t)

EMIC:setInput(DEV:_middleware/Detectors/ThresholdDetector/ThresholdDetector.emic,
              name=Pipe_alarm, inputFn=Pipe_scale_getOutput,
              outputFn=eOverTemperature, threshold=8000, hysteresis=200,
              dataType=int32_t)
```

#### Flujo de datos resultante

```
LM35_readRaw ──► MovingAvg(8) ──► LinearScale(×3.663) ──► ThresholdDet(80°C) ──► eOverTemp
   [driver]      [middleware]       [middleware]             [middleware]          [API event]
   ADC raw       suavizado          centesimas °C           detecta >80°C        alarma
```

#### program.xml del integrador

```xml
<!-- Cuando el detector de umbral cruza el limite -->
<emic-event name="eThresholdCrossed_Pipe_alarm">
    <!-- Encender LED de alarma -->
    <emic-function name="setLed_Alarm">
        <emic-function-parameter type="uint8_t">
            <emic-literal-numerical value="1"/>
        </emic-function-parameter>
    </emic-function>
    <!-- Enviar valor por EMICBus -->
    <emic-function name="sendValue">
        <emic-function-parameter type="int32_t">
            <emic-function name="getOutput_Pipe_alarm"/>
        </emic-function-parameter>
    </emic-function>
</emic-event>

<!-- Cada segundo, leer temperatura calibrada -->
<emic-event name="etOut1">
    <emic-function name="pI2C">
        <emic-function-parameter type="concat">
            <emic-literal-string value="T\t$s"/>
            <emic-function name="getOutput_Pipe_scale"/>
        </emic-function-parameter>
    </emic-function>
</emic-event>
```

---

### 8.5. Cola FIFO — Buffering de Comunicacion

**Caso de uso**: Un driver UART recibe bytes a alta velocidad. Se necesita
bufferear antes de que el protocolo Modbus los procese.

#### FIFO.emic

```
EMIC:json(type = middleware)
{
    "name": "FIFO",
    "category": "Queues",
    "brief": "Cola FIFO de tamaño fijo",
    "description": "Buffer circular que almacena datos entre un productor
                    (entrada) y un consumidor (salida). Util para desacoplar
                    tasas de datos diferentes entre capas.",
    "parameters": [
        {
            "name": "bufferSize",
            "type": "uint16_t",
            "default": "64",
            "brief": "Tamaño del buffer en elementos",
            "options": ["16", "32", "64", "128", "256"],
            "runtime": false
        }
    ],
    "input": {
        "type": "numeric",
        "accepts": ["uint8_t", "int16_t", "int32_t", "uint16_t"],
        "brief": "Dato a encolar (byte, word, etc.)"
    },
    "output": {
        "type": "numeric",
        "produces": "same_as_input",
        "brief": "Dato desencolado",
        "mode": "continuous"
    },
    "provides": {
        "functions": [
            {
                "name": "getOutput_{name}",
                "signature": "{dataType} getOutput_{name}(void)",
                "brief": "Retorna el ultimo dato desencolado"
            },
            {
                "name": "getCount_{name}",
                "signature": "uint16_t getCount_{name}(void)",
                "brief": "Retorna la cantidad de elementos en la cola"
            },
            {
                "name": "flush_{name}",
                "signature": "void flush_{name}(void)",
                "brief": "Vacia la cola",
                "runtime": true
            }
        ],
        "variables": [
            {
                "name": "fifoCount_{name}",
                "type": "uint16_t",
                "brief": "Cantidad actual de elementos en la cola"
            }
        ]
    }
}

EMIC:ifdef name
    EMIC:copy(inc/FIFO.h > TARGET:inc/FIFO_.{name}..h,
              name=.{name}., inputFn=.{inputFn}., outputFn=.{outputFn}.,
              bufferSize=.{bufferSize}., dataType=.{dataType}.)

    EMIC:copy(src/FIFO.c > TARGET:FIFO_.{name}..c,
              name=.{name}., inputFn=.{inputFn}., outputFn=.{outputFn}.,
              bufferSize=.{bufferSize}., dataType=.{dataType}.)

    EMIC:define(main_includes.FIFO_.{name}.,FIFO_.{name}.)
    EMIC:define(c_modules.FIFO_.{name}.,FIFO_.{name}.)
    EMIC:define(inits.FIFO_.{name}.,FIFO_.{name}._init)
    EMIC:define(polls.FIFO_.{name}.,FIFO_.{name}._poll)
EMIC:endif
```

#### FIFO.c (template)

```c
#include "inc/FIFO_.{name}..h"

#define BUFFER_SIZE_.{name}. .{bufferSize}.

static .{dataType}. buffer_.{name}.[BUFFER_SIZE_.{name}.];
static uint16_t head_.{name}. = 0;
static uint16_t tail_.{name}. = 0;
uint16_t fifoCount_.{name}. = 0;
static .{dataType}. lastOutput_.{name}. = 0;

void FIFO_.{name}._init(void) {
    head_.{name}. = 0;
    tail_.{name}. = 0;
    fifoCount_.{name}. = 0;
    lastOutput_.{name}. = 0;
}

void FIFO_.{name}._poll(void) {
    // Intentar leer de la entrada si hay espacio
    if (fifoCount_.{name}. < BUFFER_SIZE_.{name}.) {
        .{dataType}. value = .{inputFn}.();
        if (value != 0) {   // convencion: 0 = sin dato
            buffer_.{name}.[head_.{name}.] = value;
            head_.{name}. = (head_.{name}. + 1) % BUFFER_SIZE_.{name}.;
            fifoCount_.{name}.++;
        }
    }

    // Intentar escribir a la salida si hay datos
    if (fifoCount_.{name}. > 0) {
        .{dataType}. value = buffer_.{name}.[tail_.{name}.];
        lastOutput_.{name}. = value;
        .{outputFn}.(value);
        tail_.{name}. = (tail_.{name}. + 1) % BUFFER_SIZE_.{name}.;
        fifoCount_.{name}.--;
    }
}

.{dataType}. getOutput_.{name}.(void) {
    return lastOutput_.{name}.;
}

EMIC:ifdef usedFunction.getCount_.{name}.
uint16_t getCount_.{name}.(void) {
    return fifoCount_.{name}.;
}
EMIC:endif

EMIC:ifdef usedFunction.flush_.{name}.
void flush_.{name}.(void) {
    FIFO_.{name}._init();
}
EMIC:endif
```

**Flujo de datos** (instancia: name=RxBuf, bufferSize=64):
```
UART_readByte() ──► FIFO(64 bytes) ──► Modbus_processByte()
    [driver]         [middleware]          [API/protocolo]
```

---

## 9. Reglas de la Capa _middleware

### 9.1. Restricciones de acceso

1. **PROHIBIDO** acceder a HAL (`_hal/`) o hard (`_hard/`). El middleware
   solo consume funciones publicadas por drivers y APIs.

2. **PROHIBIDO** incluir headers de HAL (`gpio.h`, `spi.h`, `uart.h`, etc.)
   ni usar registros de hardware (`TRIS*`, `LAT*`, `SPI*BUF`, etc.).

3. **PERMITIDO** consumir funciones de drivers (como entrada) y de APIs
   (como salida). Las funciones se reciben como parametros, no por include.

4. **PERMITIDO** incluir headers de sistema (`_system/`) como streams,
   conversiones, tipos comunes.

### 9.2. Ejecucion no-bloqueante

5. **OBLIGATORIO** que `poll()` retorne rapidamente (microsegundos).
   Usar flags y variables de estado, nunca `delay()` ni `while` bloqueante.

6. **PROHIBIDO** usar `__delay_ms()`, `__delay_us()`, `for(;;)`,
   `while(1)` (excepto el patron `while(condition)` con salida garantizada).

### 9.3. Multi-instancia

7. **OBLIGATORIO** soportar multiples instancias via parametro `name=`.
   Todas las variables, funciones y archivos deben incluir `.{name}.` en
   sus nombres.

8. **PROHIBIDO** usar variables globales sin prefijo de instancia. Cada
   instancia debe tener su propio estado:
   ```c
   // CORRECTO:
   static int32_t buffer_.{name}.[WINDOW_SIZE];
   static uint8_t index_.{name}. = 0;

   // INCORRECTO:
   static int32_t buffer[WINDOW_SIZE];  // colision entre instancias
   ```

### 9.4. Interfaz estandar

9. **OBLIGATORIO** exponer al menos estas funciones:
   - `{Component}_{name}_init()` — inicializacion
   - `{Component}_{name}_poll()` — ciclo de procesamiento
   - `{name}_getOutput()` — ultimo valor procesado (para encadenamiento)

10. **OBLIGATORIO** incluir un bloque `EMIC:json(type = middleware)` con
    la metadata completa del componente (ver seccion 6).

11. **RECOMENDADO** exponer funciones de reconfiguracion en runtime con
    Discovery tags si el integrador necesita ajustar parametros desde
    `program.xml` (ej: `setThreshold_{name}()`). Marcarlas con
    `"runtime": true` en el JSON.

### 9.5. Estructura del archivo .emic

12. **OBLIGATORIO** separar el `.emic` en dos secciones:
    - **Seccion Discovery**: `EMIC:json(type = middleware)` — siempre se ejecuta
    - **Seccion Generacion**: `EMIC:ifdef name ... EMIC:endif` — solo con parametros

13. **OBLIGATORIO** que la seccion de generacion registre `c_modules`,
    `main_includes`, `inits` y `polls` del middleware:
    ```
    EMIC:define(main_includes.Component_.{name}.,Component_.{name}.)
    EMIC:define(c_modules.Component_.{name}.,Component_.{name}.)
    EMIC:define(inits.Component_.{name}.,Component_.{name}._init)
    EMIC:define(polls.Component_.{name}.,Component_.{name}._poll)
    ```

### 9.6. Funcionalidad opt-in

14. **RECOMENDADO** usar `EMIC:ifdef usedFunction.*` para funciones opcionales:
    ```c
    EMIC:ifdef usedFunction.setThreshold_.{name}.
    void setThreshold_.{name}.(int32_t newThreshold);
    EMIC:endif
    ```

15. **RECOMENDADO** usar `EMIC:ifdef usedEvent.*` para eventos propios:
    ```c
    EMIC:ifdef usedEvent.eThresholdCrossed_.{name}.
    extern void eThresholdCrossed_.{name}.(int32_t value);
    EMIC:endif
    ```

16. **OBLIGATORIO** que las funciones/variables/eventos declarados en
    `provides` del JSON se correspondan exactamente con las declaraciones
    en el `.h` (incluyendo los placeholders `{name}` y `{dataType}`).

### 9.7. Doble mecanismo de salida

17. **OBLIGATORIO** llamar a `outputFn` (conexion directa seleccionada por
    el integrador) en la condicion principal del poll.

18. **RECOMENDADO** ademas del `outputFn`, declarar eventos propios
    (`eComponentAction_{name}`) para que el integrador pueda implementar
    logica adicional en `program.xml`.

### 9.8. Retrocompatibilidad

19. **PROHIBIDO** modificar firmas de funciones de entrada/salida existentes.
    El middleware debe adaptarse a las funciones que le conectan, no al reves.

20. **OBLIGATORIO** mantener la funcion `getOutput_{name}()` con firma
    `.{dataType}. getOutput_{name}(void)` para permitir encadenamiento
    entre middlewares.

---

## 10. Impacto en el DevAgent

### Cambios necesarios en el sistema EMIC

#### 10.1. Discovery

- Nuevo parser para bloques `EMIC:json(type = middleware)`: extraer metadata,
  validar estructura, generar inventario de middleware disponibles.
- Nuevo clasificador de funciones I/O: escanear APIs del modulo, identificar
  funciones getter (entrada) y funciones/eventos de un parametro (salida),
  clasificar por tipo de dato.
- Nuevo modelo de datos: `MiddlewareDefinition` con name, category, parameters,
  input spec, output spec, provided functions/variables/events.

#### 10.2. Editor EMIC (sidebar)

- Nueva seccion **"Middleware Disponibles"**: lista de middleware indexados por
  Discovery, con nombre, brief, icono de categoria.
- Nueva seccion **"Middleware Instanciados"**: lista de instancias creadas por
  el integrador, con funciones/variables/eventos expandidos.
- **Dialogo de instanciacion**: formulario con nombre, selector de entrada
  (funciones compatibles), selector de salida (funciones/eventos compatibles),
  tipo de dato autodetectado, parametros editables con valores por defecto.
- **Validacion en tiempo real**: verificar unicidad de nombre, compatibilidad
  de tipos, parametros dentro de rango.

#### 10.3. Generate

- Nuevo paso en el pipeline de generacion: para cada instancia de middleware
  registrada por el integrador, generar la invocacion `EMIC:setInput` con
  todos los parametros resueltos e inyectarla en el flujo de compilacion.
- Manejo de orden de polls: los polls de middleware deben ejecutarse en el
  orden correcto cuando hay encadenamiento (el filtro antes del detector).

### Cambios en el DevAgent (agente de IA)

#### 10.4. Menu de clasificacion

Agregar opcion "Middleware EMIC" en el Nivel 1 del menu, con sub-opciones:

| Nivel 2 | Nivel 3 (ejemplos) |
|---------|-------------------|
| Filtro | Promedio movil, IIR, FIR, Mediana, Kalman |
| Detector | Umbral, Cruce por cero, Picos, Ventana |
| Cola / Buffer | FIFO, Circular, Prioridad |
| Conversor | Escala lineal, Tabla lookup, Unidades |
| Control | PID, Histeresis, Rate limiter, Rampa |
| Otro | (descripcion libre) |

#### 10.5. Nuevo agente generador

`MiddlewareGeneratorAgent` que genere los 3 archivos (`.emic`, `.h`, `.c`)
siguiendo las reglas de la capa:

- Genera el bloque `EMIC:json(type = middleware)` con metadata completa
- Genera el `.h` con Discovery tags y funciones protegidas por `EMIC:ifdef`
- Genera el `.c` con la logica de procesamiento y multi-instancia
- Valida que no haya accesos a HAL/hard

#### 10.6. Validador

Extender `LayerSeparationValidator` para verificar que archivos en
`_middleware/` no incluyan headers de HAL ni hard, no usen registros de
hardware, y cumplan con la interfaz estandar (init, poll, getOutput).

#### 10.7. SDK Scanner

Extender `SdkScanner` para enumerar componentes middleware existentes en
`_middleware/`, parseando los bloques `EMIC:json(type = middleware)`.

#### 10.8. Templates

Crear `MiddlewareTemplate` con plantillas base para cada tipo de componente
(filtro, detector, cola, conversor, control), incluyendo el JSON de metadata
pre-llenado y la estructura de archivos.

### Nuevo enum sugerido

```csharp
public enum MiddlewareType
{
    Unknown,
    Filter,          // Filtros (promedio movil, IIR, FIR, mediana)
    Detector,        // Detectores (umbral, cruce por cero, picos)
    Queue,           // Colas y buffers (FIFO, circular, prioridad)
    Converter,       // Conversores (escala lineal, tabla, unidades)
    Control,         // Control (PID, histeresis, rate limiter)
    Other
}
```

---

## 11. Glosario

| Termino | Definicion |
|---------|-----------|
| **Middleware** | Bloque de procesamiento intermedio entre driver y API, seleccionable por el integrador desde el editor EMIC |
| **inputFn** | Funcion de lectura que provee datos crudos (del driver, API u otro middleware). Seleccionada por el integrador de una lista de funciones compatibles |
| **outputFn** | Funcion de escritura/evento que recibe datos procesados (de API u otro middleware). Seleccionada por el integrador de una lista de funciones compatibles |
| **getOutput()** | Funcion publica que retorna el ultimo valor procesado, usada para encadenar un middleware con otro |
| **EMIC:json(type=middleware)** | Bloque de metadatos JSON en el `.emic` que describe un middleware para el proceso Discovery. Contiene identificacion, parametros, tipos I/O y funciones que expone |
| **Funcion compatible** | Funcion de API o driver cuya firma coincide con el tipo de dato de entrada o salida del middleware. Discovery las clasifica automaticamente |
| **Parametro runtime** | Parametro del middleware que puede ser modificado en runtime via `program.xml` (ej: threshold, hysteresis). Marcado con `"runtime": true` en el JSON |
| **Parametro compile-time** | Parametro del middleware que se resuelve en compile-time y no puede cambiar (ej: windowSize, bufferSize, divisor) |
| **Instanciacion** | Accion del integrador al seleccionar un middleware, asignarle nombre, conectar funciones I/O y configurar parametros desde el dialogo del editor |
| **Evento propio** | Evento EMIC declarado por el middleware (ej: `eThresholdCrossed_{name}`) que el integrador puede usar en `program.xml` para logica adicional. Protegido por `EMIC:ifdef usedEvent.*` |
| **Doble salida** | Mecanismo por el cual un middleware llama tanto a `outputFn` (conexion directa) como a sus eventos propios (logica adicional), permitiendo al integrador usar ambos |
| **Encadenamiento** | Conexion de la salida de un middleware con la entrada de otro, usando `getOutput_{name1}` como `inputFn` del segundo. Permite pipelines de procesamiento |
| **Zero overhead** | Las conexiones entre middleware y funciones I/O se resuelven en compile-time (sustitucion de macros), sin punteros a funcion ni indirecciones en runtime |
| **Multi-instancia** | Soporte para N instancias independientes de un mismo tipo de middleware, cada una con nombre unico, estado propio y conexiones diferentes |
| **Discovery** | Proceso automatico del sistema EMIC que parsea los componentes del modulo, extrae metadata y genera el inventario del sidebar del editor |
| **Sidebar** | Panel lateral del editor EMIC que muestra funciones, eventos, variables, middleware disponibles y middleware instanciados del modulo actual |
