# Capa _middleware — Diseño de Bloques de Procesamiento Intermedios

> Documento de diseño para una nueva capa del SDK EMIC que aloja funciones de
> acondicionamiento de señal, filtros, colas, detectores, conversiones y otros
> bloques de procesamiento que actuan como intermediarios conectables entre
> drivers y APIs.

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

### La solucion

Una nueva capa `_middleware/` que contiene **bloques de procesamiento
independientes, parametrizables y conectables**. Cada bloque:

- Tiene una **entrada** (funcion de lectura, provista por un driver o API)
- Tiene una **salida** (funcion de escritura/evento, provista por una API)
- Es **parametrizable** (ventana de filtro, umbral, histeresis, etc.)
- Es **multi-instancia** (multiples filtros con diferentes configuraciones)
- Opera de forma **no-bloqueante** (poll-based, como toda la arquitectura EMIC)
- **NO accede a HAL ni hard** — solo consume funciones expuestas por otras capas

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
- APIs **invocan/instancian** middleware (no al reves)

---

## 3. Estructura de Archivos

```
_middleware/
├── Filters/
│   ├── MovingAverage/
│   │   ├── MovingAverage.emic
│   │   ├── inc/
│   │   │   └── MovingAverage.h
│   │   └── src/
│   │       └── MovingAverage.c
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
- `.emic` — orquestador (dependencias, copy, registro)
- `inc/*.h` — interfaz, Discovery metadata, registro poll
- `src/*.c` — implementacion

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

- **name**: Identificador unico de la instancia (como timer_api con name=1)
- **input**: Funcion que provee el dato crudo (ej: `LM35_readRaw`)
- **output**: Funcion que recibe el dato procesado (ej: `eTemperatureAlarm`)
- **parametros**: Constantes de configuracion sustituidas en compile-time
- **poll**: Funcion no-bloqueante que ejecuta el ciclo input → proceso → output

### Diferencia clave con APIs

| Aspecto | API | Middleware |
|---------|-----|-----------|
| Registra init/poll | Si, en main loop | Depende de la variante (ver abajo) |
| Accede a HAL/hard | Via drivers | Nunca |
| Expone funciones al integrador | Si (Discovery) | No (*) |
| Quien la invoca | El modulo (generate.emic) | La API o el modulo |
| Instanciacion | Una vez por API | Multiples instancias con name= |
| I/O | Define sus propias funciones | Conecta funciones de otras capas |

(*) El middleware podria opcionalmente exponer funciones al Discovery si se
desea que el integrador pueda reconfigurarlo desde program.xml (ej:
`setThreshold()`). Esto es una decision de diseño por componente.

---

## 5. Variantes de Conexion

### 5.1. Variante A — Parametros Inline

La API incluye al middleware pasando nombres de funciones I/O como parametros
de `EMIC:setInput`. El middleware usa `EMIC:copy` con sustitucion de macros
para generar codigo con las funciones correctas.

**Quien conecta**: La API (en su .emic)
**Cuando se resuelve**: Compile-time (sustitucion de macros)

#### Ejemplo: Filtro promedio movil en API de temperatura

**Temperature.emic** (API):
```
// Incluir driver
EMIC:setInput(DEV:_drivers/Sensors/LM35/LM35.emic, pin=AN0)

// Incluir middleware: filtro conectado entre driver y API
EMIC:setInput(DEV:_middleware/Filters/MovingAverage/MovingAverage.emic,
              name=TempFilter,
              inputFn=LM35_readRaw,
              outputFn=Temperature_onFiltered,
              windowSize=8,
              dataType=int32_t)

// Copiar archivos propios de la API
EMIC:setOutput(TARGET:inc/Temperature.h)
EMIC:setInput(inc/Temperature.h)
EMIC:restoreOutput
EMIC:copy(src/Temperature.c > TARGET:Temperature.c, filter=TempFilter)

EMIC:define(main_includes.Temperature,Temperature)
EMIC:define(c_modules.Temperature,Temperature)
```

**MovingAverage.emic** (Middleware):
```
// Sin dependencias de HAL/hard — solo genera archivos
EMIC:copy(inc/MovingAverage.h > TARGET:inc/MovingAverage_.{name}..h,
          name=.{name}.,
          inputFn=.{inputFn}.,
          outputFn=.{outputFn}.,
          windowSize=.{windowSize}.,
          dataType=.{dataType}.)

EMIC:copy(src/MovingAverage.c > TARGET:MovingAverage_.{name}..c,
          name=.{name}.,
          inputFn=.{inputFn}.,
          outputFn=.{outputFn}.,
          windowSize=.{windowSize}.,
          dataType=.{dataType}.)

EMIC:define(main_includes.MovingAverage_.{name}.,MovingAverage_.{name}.)
EMIC:define(c_modules.MovingAverage_.{name}.,MovingAverage_.{name}.)
```

**MovingAverage.h** (template):
```c
#ifndef _MOVING_AVERAGE_.{name}._H_
#define _MOVING_AVERAGE_.{name}._H_

#include <stdint.h>

// Forward declarations de funciones externas
extern .{dataType}. .{inputFn}.(void);
extern void .{outputFn}.(.{dataType}. value);

// Funcion de procesamiento
void MovingAverage_.{name}._init(void);
void MovingAverage_.{name}._poll(void);

// Registro de init y poll
EMIC:define(inits.MovingAverage_.{name}.,MovingAverage_.{name}._init)
EMIC:define(polls.MovingAverage_.{name}.,MovingAverage_.{name}._poll)

#endif
```

**MovingAverage.c** (template):
```c
#include "inc/MovingAverage_.{name}..h"

#define WINDOW_SIZE .{windowSize}.

static .{dataType}. buffer_.{name}.[WINDOW_SIZE];
static uint8_t index_.{name}. = 0;
static .{dataType}. accumulator_.{name}. = 0;
static uint8_t count_.{name}. = 0;

void MovingAverage_.{name}._init(void) {
    uint8_t i;
    for (i = 0; i < WINDOW_SIZE; i++) {
        buffer_.{name}.[i] = 0;
    }
    index_.{name}. = 0;
    accumulator_.{name}. = 0;
    count_.{name}. = 0;
}

void MovingAverage_.{name}._poll(void) {
    // 1. Leer entrada (funcion del driver)
    .{dataType}. raw = .{inputFn}.();

    // 2. Procesar (filtro promedio movil)
    accumulator_.{name}. -= buffer_.{name}.[index_.{name}.];
    buffer_.{name}.[index_.{name}.] = raw;
    accumulator_.{name}. += raw;
    index_.{name}.++;
    if (index_.{name}. >= WINDOW_SIZE) index_.{name}. = 0;
    if (count_.{name}. < WINDOW_SIZE) count_.{name}.++;

    .{dataType}. filtered = accumulator_.{name}. / count_.{name}.;

    // 3. Escribir salida (funcion de la API)
    .{outputFn}.(filtered);
}
```

**Resultado expandido** (despues de sustitucion con name=TempFilter,
inputFn=LM35_readRaw, outputFn=Temperature_onFiltered, windowSize=8):
```c
// MovingAverage_TempFilter.c
#include "inc/MovingAverage_TempFilter.h"

#define WINDOW_SIZE 8

static int32_t buffer_TempFilter[8];
static uint8_t index_TempFilter = 0;
static int32_t accumulator_TempFilter = 0;
static uint8_t count_TempFilter = 0;

void MovingAverage_TempFilter_init(void) { /* ... */ }

void MovingAverage_TempFilter_poll(void) {
    int32_t raw = LM35_readRaw();                   // Lee del driver
    // ... filtrado ...
    int32_t filtered = accumulator_TempFilter / count_TempFilter;
    Temperature_onFiltered(filtered);                // Escribe a la API
}
```

**Temperature.c** (la API recibe el dato filtrado):
```c
#include "inc/Temperature.h"
#include "inc/MovingAverage_TempFilter.h"

static float current_temperature;

// Esta funcion es llamada por el middleware en cada poll
void Temperature_onFiltered(int32_t filtered_adc) {
    // Convertir ADC a grados (especifico de la API)
    current_temperature = (float)filtered_adc * 0.01f;

    EMIC:ifdef usedEvent.eTemperatureReady
    eTemperatureReady(current_temperature);
    EMIC:endif
}

float getTemperature(void) {
    return current_temperature;
}
```

#### Pros y contras

| Ventaja | Desventaja |
|---------|------------|
| Sintaxis familiar (como driver= en USB_API) | La API debe conocer nombres de funciones del driver |
| Todo se resuelve en compile-time | El middleware queda acoplado a la API que lo incluye |
| Zero overhead en runtime | Cambiar las conexiones requiere editar el .emic de la API |
| Multi-instancia natural (name=) | No se puede reconectar desde el modulo sin modificar la API |

---

### 5.2. Variante B — Invocacion Separada + Wiring por Macros

El modulo invoca al middleware de forma independiente en su `generate.emic`,
y luego define macros de 3 niveles para conectar entrada y salida. El middleware
lee esas macros durante la expansion.

**Quien conecta**: El modulo (generate.emic)
**Cuando se resuelve**: Compile-time (macros de 3 niveles: misMacros3)

#### Ejemplo: Detector de umbral conectado a sensor + alarma

**generate.emic** (Modulo):
```
// Hardware
EMIC:setInput(DEV:_pcb/pcb.emic, pcb=HRD_TEMP_SENSOR_V1)

// APIs y Drivers
EMIC:setInput(DEV:_api/Sensors/Temperature/Temperature.emic, driver=LM35)
EMIC:setInput(DEV:_api/Indicators/LEDs/led.emic, name=Alarm, pin=Led1)
EMIC:setInput(DEV:_api/Wired_Communication/EMICBus/EMICBus.emic, port=2, frameID=0)

// Middleware: detector de umbral (invocacion independiente)
EMIC:setInput(DEV:_middleware/Detectors/ThresholdDetector/ThresholdDetector.emic,
              name=TempAlarm,
              threshold=80,
              hysteresis=5,
              dataType=int32_t)

// Wiring: conectar entrada y salida del middleware via macros
EMIC:define(mw.TempAlarm.input,  getTemperatureRaw)
EMIC:define(mw.TempAlarm.output, eHighTemperature)
```

**ThresholdDetector.emic** (Middleware):
```
EMIC:copy(inc/ThresholdDetector.h > TARGET:inc/ThresholdDetector_.{name}..h,
          name=.{name}.,
          threshold=.{threshold}.,
          hysteresis=.{hysteresis}.,
          dataType=.{dataType}.)

EMIC:copy(src/ThresholdDetector.c > TARGET:ThresholdDetector_.{name}..c,
          name=.{name}.,
          threshold=.{threshold}.,
          hysteresis=.{hysteresis}.,
          dataType=.{dataType}.)

EMIC:define(main_includes.ThresholdDetector_.{name}.,ThresholdDetector_.{name}.)
EMIC:define(c_modules.ThresholdDetector_.{name}.,ThresholdDetector_.{name}.)
```

**ThresholdDetector.h** (template):
```c
#ifndef _THRESHOLD_DETECTOR_.{name}._H_
#define _THRESHOLD_DETECTOR_.{name}._H_

#include <stdint.h>

// Las funciones de I/O se resuelven desde macros mw.{name}.input / mw.{name}.output
// declaradas por el modulo en generate.emic
extern .{dataType}. .{mw..{name}..input}.(void);
extern void .{mw..{name}..output}.(.{dataType}. value);

void ThresholdDetector_.{name}._init(void);
void ThresholdDetector_.{name}._poll(void);

EMIC:define(inits.ThresholdDetector_.{name}.,ThresholdDetector_.{name}._init)
EMIC:define(polls.ThresholdDetector_.{name}.,ThresholdDetector_.{name}._poll)

/**
* @fn void setThreshold_.{name}.(int32_t newThreshold);
* @alias SetThreshold_.{name}.
* @brief Modifica el umbral en runtime
* @param newThreshold Nuevo valor de umbral
*/
void setThreshold_.{name}.(.{dataType}. newThreshold);

#endif
```

**ThresholdDetector.c** (template):
```c
#include "inc/ThresholdDetector_.{name}..h"

static .{dataType}. threshold_.{name}. = .{threshold}.;
static .{dataType}. hysteresis_.{name}. = .{hysteresis}.;
static uint8_t active_.{name}. = 0;

void ThresholdDetector_.{name}._init(void) {
    active_.{name}. = 0;
}

void ThresholdDetector_.{name}._poll(void) {
    // 1. Leer entrada (funcion resuelta por macro mw.{name}.input)
    .{dataType}. value = .{mw..{name}..input}.();

    // 2. Detectar cruce de umbral con histeresis
    if (!active_.{name}.) {
        if (value >= threshold_.{name}.) {
            active_.{name}. = 1;
            // 3. Notificar salida (funcion resuelta por macro mw.{name}.output)
            .{mw..{name}..output}.(value);
        }
    } else {
        if (value < (threshold_.{name}. - hysteresis_.{name}.)) {
            active_.{name}. = 0;
        }
    }
}

void setThreshold_.{name}.(.{dataType}. newThreshold) {
    threshold_.{name}. = newThreshold;
}
```

**Resolucion de macros** (paso a paso):

1. `.{name}.` se resuelve a `TempAlarm` (parametro de EMIC:setInput)
2. `.{mw..{name}..input}.` → `.{mw.TempAlarm.input}.` → busca en misMacros3
   → encuentra `getTemperatureRaw` (definido en generate.emic)
3. `.{mw..{name}..output}.` → `.{mw.TempAlarm.output}.` → `eHighTemperature`

**Resultado expandido**:
```c
void ThresholdDetector_TempAlarm_poll(void) {
    int32_t value = getTemperatureRaw();
    if (!active_TempAlarm) {
        if (value >= threshold_TempAlarm) {
            active_TempAlarm = 1;
            eHighTemperature(value);
        }
    } else {
        if (value < (threshold_TempAlarm - hysteresis_TempAlarm)) {
            active_TempAlarm = 0;
        }
    }
}
```

**Nota sobre misMacros3**: El sistema de macros de 3 niveles (`misMacros3`)
ya esta implementado en TreeMaker.cs (ver MEMORY.md). La clave `mw.TempAlarm.input`
se almacena como `misMacros3["mw"]["TempAlarm"]["input"] = "getTemperatureRaw"`.
La sintaxis `.{mw..{name}..input}.` primero resuelve `.{name}.` a `TempAlarm`,
y luego resuelve `.{mw.TempAlarm.input}.` como macro de 3 niveles.

#### Pros y contras

| Ventaja | Desventaja |
|---------|------------|
| Desacoplamiento total: el modulo decide las conexiones | Requiere macros de 3 niveles (misMacros3) |
| La API no necesita saber del middleware | Sintaxis mas compleja para el integrador |
| Se puede reconectar desde generate.emic sin tocar APIs | La resolucion `.{mw..{name}..X}.` puede confundir |
| Middleware es completamente reutilizable | Errores de wiring son dificiles de diagnosticar |
| El integrador ve el middleware como un componente independiente | Necesita documentacion clara del protocolo de macros |

---

### 5.3. Variante C — Function Pointers (Patron Stream)

El middleware define tipos con punteros a funcion (como `streamOut_t` y
`streamIn_t` de `_system/Stream/`). La API crea instancias del middleware
en su `init()` pasando las funciones de entrada y salida como argumentos.

**Quien conecta**: La API (en su codigo C, dentro de init)
**Cuando se resuelve**: Runtime (init-time, no dynamicamente)

#### Ejemplo: Cola FIFO entre driver de comunicacion y API

**FIFO.emic** (Middleware):
```
// Sin dependencias — libreria pura
EMIC:copy(inc/FIFO.h > TARGET:inc/FIFO_.{name}..h,
          name=.{name}.,
          bufferSize=.{bufferSize}.,
          dataType=.{dataType}.)

EMIC:copy(src/FIFO.c > TARGET:FIFO_.{name}..c,
          name=.{name}.,
          bufferSize=.{bufferSize}.,
          dataType=.{dataType}.)

EMIC:define(main_includes.FIFO_.{name}.,FIFO_.{name}.)
EMIC:define(c_modules.FIFO_.{name}.,FIFO_.{name}.)
```

**FIFO.h** (template):
```c
#ifndef _FIFO_.{name}._H_
#define _FIFO_.{name}._H_

#include <stdint.h>

// Tipo: punteros a funcion para I/O
typedef .{dataType}. (*FIFO_.{name}._ReadFn)(void);
typedef void (*FIFO_.{name}._WriteFn)(.{dataType}. value);

// Estructura de la instancia
typedef struct {
    FIFO_.{name}._ReadFn readInput;
    FIFO_.{name}._WriteFn writeOutput;
    .{dataType}. buffer[.{bufferSize}.];
    uint16_t head;
    uint16_t tail;
    uint16_t count;
} FIFO_.{name}._t;

// Singleton de la instancia
extern FIFO_.{name}._t fifo_.{name}.;

// API publica
void FIFO_.{name}._init(FIFO_.{name}._ReadFn readFn, FIFO_.{name}._WriteFn writeFn);
void FIFO_.{name}._poll(void);
uint16_t FIFO_.{name}._getCount(void);
void FIFO_.{name}._flush(void);

// No registra init/poll automaticamente — la API que lo usa
// lo llama desde su propio init/poll.

#endif
```

**FIFO.c** (template):
```c
#include "inc/FIFO_.{name}..h"

FIFO_.{name}._t fifo_.{name}.;

void FIFO_.{name}._init(FIFO_.{name}._ReadFn readFn, FIFO_.{name}._WriteFn writeFn) {
    fifo_.{name}..readInput = readFn;
    fifo_.{name}..writeOutput = writeFn;
    fifo_.{name}..head = 0;
    fifo_.{name}..tail = 0;
    fifo_.{name}..count = 0;
}

void FIFO_.{name}._poll(void) {
    // Intentar leer de la entrada si hay espacio
    if (fifo_.{name}..count < .{bufferSize}.) {
        .{dataType}. value = fifo_.{name}..readInput();
        if (value != 0) {  // convencion: 0 = sin dato
            fifo_.{name}..buffer[fifo_.{name}..head] = value;
            fifo_.{name}..head = (fifo_.{name}..head + 1) % .{bufferSize}.;
            fifo_.{name}..count++;
        }
    }

    // Intentar escribir a la salida si hay datos
    if (fifo_.{name}..count > 0) {
        .{dataType}. value = fifo_.{name}..buffer[fifo_.{name}..tail];
        fifo_.{name}..writeOutput(value);
        fifo_.{name}..tail = (fifo_.{name}..tail + 1) % .{bufferSize}.;
        fifo_.{name}..count--;
    }
}

uint16_t FIFO_.{name}._getCount(void) {
    return fifo_.{name}..count;
}

void FIFO_.{name}._flush(void) {
    fifo_.{name}..head = 0;
    fifo_.{name}..tail = 0;
    fifo_.{name}..count = 0;
}
```

**Uso desde la API** (ModbusGateway.emic incluye al middleware):
```
EMIC:setInput(DEV:_middleware/Queues/FIFO/FIFO.emic,
              name=RxBuffer, bufferSize=64, dataType=uint8_t)
```

**Uso desde la API** (ModbusGateway.c conecta en init):
```c
#include "inc/FIFO_RxBuffer.h"
#include "inc/UART_Driver.h"

void ModbusGateway_init(void) {
    UART_Driver_init();
    // Conectar middleware: UART lee → FIFO → Gateway procesa
    FIFO_RxBuffer_init(UART_readByte, ModbusGateway_onByteReceived);
}

void ModbusGateway_poll(void) {
    // El middleware se encarga del buffering
    FIFO_RxBuffer_poll();
    // ... logica de protocolo Modbus ...
}
```

**Diferencia clave**: El middleware NO registra su poll en el main loop.
La API lo llama explicitamente desde su propio poll, controlando el orden.

#### Pros y contras

| Ventaja | Desventaja |
|---------|------------|
| Maximo desacoplamiento (como Stream) | Overhead de runtime (function pointers) |
| La conexion se puede cambiar en runtime (reconfigurable) | Mas RAM (punteros en struct + instancia) |
| Patron familiar para programadores C | La API debe gestionar el ciclo de vida |
| Se puede testear unitariamente con mocks | Menos optimizable por el compilador |
| No requiere macros complejas | El middleware no aparece como componente independiente en Discovery |

---

### 5.4. Variante D — Pipeline Declarativo

El modulo declara una cadena de procesamiento completa en `generate.emic`.
Un archivo orquestador `pipeline.emic` interpreta la cadena e instancia
cada etapa conectandolas en secuencia.

**Quien conecta**: El modulo (generate.emic) con sintaxis declarativa
**Cuando se resuelve**: Compile-time (EMIC:foreach + parametros)

#### Ejemplo: Pipeline temperatura con filtro + detector + conversion

**generate.emic** (Modulo):
```
// Hardware y APIs
EMIC:setInput(DEV:_pcb/pcb.emic, pcb=HRD_TEMP_SENSOR_V1)
EMIC:setInput(DEV:_api/Sensors/Temperature/Temperature.emic, driver=LM35)
EMIC:setInput(DEV:_api/Indicators/LEDs/led.emic, name=Alarm, pin=Led1)

// Pipeline declarativo: entrada → etapa1 → etapa2 → etapa3 → salida
EMIC:define(pipe.TempPipe.input, LM35_readRaw)
EMIC:define(pipe.TempPipe.output, Temperature_onProcessed)
EMIC:define(pipe.TempPipe.dataType, int32_t)
EMIC:define(pipe.TempPipe.stage1, MovingAverage)
EMIC:define(pipe.TempPipe.stage1.windowSize, 8)
EMIC:define(pipe.TempPipe.stage2, LinearScale)
EMIC:define(pipe.TempPipe.stage2.factor, 100)
EMIC:define(pipe.TempPipe.stage2.offset, -500)
EMIC:define(pipe.TempPipe.stage3, ThresholdDetector)
EMIC:define(pipe.TempPipe.stage3.threshold, 8000)
EMIC:define(pipe.TempPipe.stage3.hysteresis, 200)
EMIC:define(pipe.TempPipe.stageCount, 3)

EMIC:setInput(DEV:_middleware/pipeline.emic, name=TempPipe)
```

**pipeline.emic** (Orquestador de middleware):
```
// Este archivo instancia cada etapa del pipeline y las conecta.
// Lee las macros pipe.{name}.* para determinar las etapas.

// -------- Etapa 1 --------
EMIC:ifdef pipe..{name}..stage1
    // Entrada de etapa 1 = entrada del pipeline
    EMIC:setInput(DEV:_middleware/.{pipe..{name}..stage1_category}./.{pipe..{name}..stage1}./.{pipe..{name}..stage1}..emic,
        name=.{name}._s1,
        inputFn=.{pipe..{name}..input}.,
        outputFn=.{name}._s1_to_s2,
        dataType=.{pipe..{name}..dataType}.)
EMIC:endif

// -------- Etapa 2 --------
EMIC:ifdef pipe..{name}..stage2
    EMIC:setInput(DEV:_middleware/.{pipe..{name}..stage2_category}./.{pipe..{name}..stage2}./.{pipe..{name}..stage2}..emic,
        name=.{name}._s2,
        inputFn=.{name}._s1_getOutput,
        outputFn=.{name}._s2_to_s3,
        dataType=.{pipe..{name}..dataType}.)
EMIC:endif

// -------- Etapa 3 --------
EMIC:ifdef pipe..{name}..stage3
    // Salida de ultima etapa = salida del pipeline
    EMIC:setInput(DEV:_middleware/.{pipe..{name}..stage3_category}./.{pipe..{name}..stage3}./.{pipe..{name}..stage3}..emic,
        name=.{name}._s3,
        inputFn=.{name}._s2_getOutput,
        outputFn=.{pipe..{name}..output}.,
        dataType=.{pipe..{name}..dataType}.)
EMIC:endif
```

**Nota**: Esta variante es la mas compleja de implementar. Requiere:
- Una convencion de nombres para funciones intermedias (`_s1_to_s2`,
  `_s2_getOutput`, etc.)
- Un mecanismo para pasar parametros especificos de cada etapa
  (`stage1.windowSize`, `stage2.threshold`, etc.)
- Un orquestador `pipeline.emic` que entienda la estructura
- Posiblemente extensiones al compilador para soportar EMIC:foreach
  sobre las etapas dinamicamente

#### Forma simplificada (sin orquestador complejo)

Una version mas pragmatica donde el modulo instancia manualmente cada
etapa pero las conecta en cadena:

**generate.emic** (conexion manual etapa por etapa):
```
// Instanciar componentes middleware
EMIC:setInput(DEV:_middleware/Filters/MovingAverage/MovingAverage.emic,
              name=TempPipe_filter,
              inputFn=LM35_readRaw,
              outputFn=TempPipe_filter_out,
              windowSize=8, dataType=int32_t)

EMIC:setInput(DEV:_middleware/Converters/LinearScale/LinearScale.emic,
              name=TempPipe_scale,
              inputFn=TempPipe_filter_getOutput,
              outputFn=TempPipe_scale_out,
              factor=100, offset=-500, dataType=int32_t)

EMIC:setInput(DEV:_middleware/Detectors/ThresholdDetector/ThresholdDetector.emic,
              name=TempPipe_alarm,
              inputFn=TempPipe_scale_getOutput,
              outputFn=eHighTemperature,
              threshold=8000, hysteresis=200, dataType=int32_t)
```

Esta forma simplificada es esencialmente la Variante A aplicada en cadena,
donde la salida de una etapa es la entrada de la siguiente. No requiere
orquestador ni macros complejas — solo la convencion de que cada middleware
expone una funcion `{name}_getOutput()` para encadenamiento.

#### Pros y contras

| Ventaja | Desventaja |
|---------|------------|
| Vision completa del procesamiento en un solo lugar | Muy compleja de implementar correctamente |
| Cadenas arbitrariamente largas | Requiere orquestador pipeline.emic sofisticado |
| El modulo tiene control total | Debugging dificil (funciones intermedias auto-generadas) |
| Patron expresivo y declarativo | Posiblemente necesita extensiones al compilador EMIC |
| La forma simplificada es pragmatica | La forma completa puede ser over-engineering |

---

## 6. Comparativa de Variantes

| Criterio | A (Inline) | B (Macros) | C (Fn Ptrs) | D (Pipeline) |
|----------|:----------:|:----------:|:-----------:|:------------:|
| **Complejidad de uso** | Baja | Media | Baja | Alta |
| **Desacoplamiento** | Bajo | Alto | Alto | Alto |
| **Overhead runtime** | Cero | Cero | Bajo (ptrs) | Cero |
| **Optimizacion compilador** | Maxima | Maxima | Limitada | Maxima |
| **Quien decide las conexiones** | API | Modulo | API | Modulo |
| **Multi-instancia** | Si | Si | Si | Si |
| **Encadenamiento** | Manual | Manual | Manual | Nativo |
| **Reconfigurable en runtime** | No | No | Si | No |
| **Compatibilidad con EMIC actual** | Total | Requiere misMacros3 | Total | Parcial (*) |
| **Dificultad de implementacion** | Baja | Media | Baja | Alta |
| **Familiar para devs EMIC** | Si (como driver=) | Moderado | Si (como Stream) | No |

(*) La forma simplificada de D es totalmente compatible; la forma completa
requiere extensiones.

### Recomendacion

**Variante A como base** para la mayoria de los casos: es la mas simple, tiene
zero overhead, y es consistente con los patrones existentes (timer con name=,
USB con driver=).

**Variante B para integracion a nivel modulo** cuando el mismo middleware debe
ser conectado entre componentes que la API no conoce. Ideal para configuraciones
complejas donde el integrador (no el desarrollador de la API) decide las conexiones.

**Variante C para casos especiales** donde se necesita reconfiguracion en runtime
o testing unitario con mocks.

**Variante D (simplificada)** como patron para documentar cadenas de procesamiento
en proyectos complejos, pero sin implementar el orquestador completo inicialmente.

---

## 7. Ejemplos Concretos

### 7.1. Filtro Promedio Movil → Sensor de Temperatura

**Caso de uso**: Un sensor LM35 produce lecturas ruidosas del ADC. Se necesita
suavizar la señal antes de que la API de temperatura la procese.

**Variante A (inline)**:
```
// Temperature.emic
EMIC:setInput(DEV:_drivers/Sensors/LM35/LM35.emic, pin=AN0)
EMIC:setInput(DEV:_middleware/Filters/MovingAverage/MovingAverage.emic,
              name=TempSmooth, inputFn=LM35_readRaw,
              outputFn=Temperature_onSmoothed, windowSize=16, dataType=int32_t)
```

**Flujo de datos**:
```
LM35_readRaw() ──► MovingAverage(16) ──► Temperature_onSmoothed()
     [driver]          [middleware]              [API]
```

### 7.2. Detector de Umbral → Alarma de Temperatura

**Caso de uso**: Generar un evento cuando la temperatura supera 80°C, con
histeresis de 5°C para evitar rebotes.

**Variante B (macros)**:
```
// generate.emic
EMIC:setInput(DEV:_api/Sensors/Temperature/Temperature.emic, driver=LM35)
EMIC:setInput(DEV:_middleware/Detectors/ThresholdDetector/ThresholdDetector.emic,
              name=HighTemp, threshold=80, hysteresis=5, dataType=int32_t)
EMIC:define(mw.HighTemp.input, getTemperature)
EMIC:define(mw.HighTemp.output, eHighTemperature)
```

**Flujo de datos**:
```
getTemperature() ──► ThresholdDetector(80,5) ──► eHighTemperature()
      [API fn]           [middleware]              [API event]
```

### 7.3. Cola FIFO → Buffering de Comunicacion

**Caso de uso**: Un driver UART recibe bytes a alta velocidad. Se necesita
bufferear antes de que el protocolo Modbus los procese.

**Variante C (function pointers)**:
```c
// ModbusGateway.c
void ModbusGateway_init(void) {
    UART_init();
    FIFO_RxBuf_init(UART_readByte, Modbus_processByte);
}

void ModbusGateway_poll(void) {
    FIFO_RxBuf_poll();   // lee UART, buferea, entrega a Modbus
    Modbus_poll();        // procesa frames completos
}
```

**Flujo de datos**:
```
UART_readByte() ──► FIFO(64 bytes) ──► Modbus_processByte()
    [driver]         [middleware]          [API/protocolo]
```

### 7.4. Conversor de Unidades → Calibracion Lineal

**Caso de uso**: Convertir lectura de ADC cruda (0-4095) a temperatura en
centesimas de grado (-5000 a +15000 = -50.00°C a +150.00°C).

**Variante A (inline)**:
```
// Temperature.emic
EMIC:setInput(DEV:_drivers/Sensors/LM35/LM35.emic, pin=AN0)
EMIC:setInput(DEV:_middleware/Converters/LinearScale/LinearScale.emic,
              name=ADCtoTemp,
              inputFn=LM35_readRaw,
              outputFn=Temperature_onCalibrated,
              factor=3663,
              offset=-5000,
              divisor=1000,
              dataType=int32_t)
```

**LinearScale.c** (formula: output = (input * factor / divisor) + offset):
```c
void LinearScale_.{name}._poll(void) {
    .{dataType}. raw = .{inputFn}.();
    .{dataType}. scaled = (raw * ((.{dataType}.).{factor}.)) / .{divisor}. + .{offset}.;
    .{outputFn}.(scaled);
}
```

**Flujo de datos**:
```
LM35_readRaw() ──► LinearScale(×3.663, -50.00) ──► Temperature_onCalibrated()
    [driver]              [middleware]                       [API]
    ADC: 0-4095      centesimas: -5000 a +15000         grados ×100
```

### 7.5. Cadena completa (Variante D simplificada)

**Caso de uso**: Filtrar ruido → convertir unidades → detectar umbral, todo
en cadena para un sensor de temperatura con alarma.

**generate.emic**:
```
EMIC:setInput(DEV:_drivers/Sensors/LM35/LM35.emic, pin=AN0)
EMIC:setInput(DEV:_api/Sensors/Temperature/Temperature.emic)

// Cadena: LM35 → filtro → conversion → detector → API
EMIC:setInput(DEV:_middleware/Filters/MovingAverage/MovingAverage.emic,
              name=Pipe_s1, inputFn=LM35_readRaw,
              outputFn=Pipe_s1_out, windowSize=8, dataType=int32_t)

EMIC:setInput(DEV:_middleware/Converters/LinearScale/LinearScale.emic,
              name=Pipe_s2, inputFn=Pipe_s1_getOutput,
              outputFn=Pipe_s2_out, factor=3663, offset=-5000,
              divisor=1000, dataType=int32_t)

EMIC:setInput(DEV:_middleware/Detectors/ThresholdDetector/ThresholdDetector.emic,
              name=Pipe_s3, inputFn=Pipe_s2_getOutput,
              outputFn=eHighTemperature, threshold=8000, hysteresis=200,
              dataType=int32_t)
```

**Flujo de datos**:
```
LM35_readRaw ──► MovingAvg(8) ──► LinearScale(×3.663) ──► ThresholdDet(80°C) ──► eHighTemp
   [driver]      [middleware]       [middleware]             [middleware]          [API event]
   ADC raw       suavizado          centesimas °C           detecta >80°C        alarma
```

**Convencion de encadenamiento**: Cada middleware expone una funcion
`{name}_getOutput()` que retorna el ultimo valor procesado, permitiendo
que la siguiente etapa la use como `inputFn`. Ademas, la funcion `outputFn`
puede ser una funcion intermediaria que simplemente almacena el valor
(para polling pull) o una funcion que dispara procesamiento inmediato
(para push). El patron recomendado es:

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

---

## 8. Reglas de la Capa _middleware

### 8.1. Restricciones de acceso

1. **PROHIBIDO** acceder a HAL (`_hal/`) o hard (`_hard/`). El middleware
   solo consume funciones publicadas por drivers y APIs.

2. **PROHIBIDO** incluir headers de HAL (`gpio.h`, `spi.h`, `uart.h`, etc.)
   ni usar registros de hardware (`TRIS*`, `LAT*`, `SPI*BUF`, etc.).

3. **PERMITIDO** consumir funciones de drivers (como entrada) y de APIs
   (como salida). Las funciones se reciben como parametros, no por include.

4. **PERMITIDO** incluir headers de sistema (`_system/`) como streams,
   conversiones, tipos comunes.

### 8.2. Ejecucion no-bloqueante

5. **OBLIGATORIO** que `poll()` retorne rapidamente (microsegundos).
   Usar flags y variables de estado, nunca `delay()` ni `while` bloqueante.

6. **PROHIBIDO** usar `__delay_ms()`, `__delay_us()`, `for(;;)`,
   `while(1)` (excepto el patron `while(condition)` con salida garantizada).

### 8.3. Multi-instancia

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

### 8.4. Interfaz estandar

9. **OBLIGATORIO** exponer al menos estas funciones:
   - `{Component}_{name}_init()` — inicializacion
   - `{Component}_{name}_poll()` — ciclo de procesamiento
   - `{name}_getOutput()` — ultimo valor procesado (para encadenamiento)

10. **RECOMENDADO** exponer funciones de reconfiguracion en runtime con
    metadata Discovery si el integrador necesita ajustar parametros desde
    `program.xml` (ej: `setThreshold_{name}()`).

### 8.5. Registro

11. **Depende de la variante**:
    - **Variantes A, B, D**: El middleware registra su propio init/poll
      (`EMIC:define(inits.X, X)` / `EMIC:define(polls.X, X)`).
    - **Variante C**: La API que lo consume llama init/poll explicitamente
      (patron de cadena).

12. **OBLIGATORIO** registrar `c_modules` y `main_includes` en todas las
    variantes para que el sistema de build incluya los archivos.

### 8.6. Retrocompatibilidad

13. **PROHIBIDO** modificar firmas de funciones de entrada/salida existentes.
    El middleware debe adaptarse a las funciones que le conectan, no al reves.

14. **RECOMENDADO** usar `EMIC:ifdef` para funcionalidad opcional:
    ```c
    EMIC:ifdef usedFunction.setThreshold_.{name}.
    void setThreshold_.{name}.(int32_t newThreshold);
    EMIC:endif
    ```

---

## 9. Impacto en el DevAgent

### Cambios necesarios

1. **Menu inicial**: Agregar opcion "Middleware EMIC" en el Nivel 1 del menu
   de clasificacion, con sub-opciones: Filtro, Detector, Cola, Conversor,
   Control, Otro.

2. **Nuevo agente generador**: `MiddlewareGeneratorAgent` que genere los
   3 archivos (.emic, .h, .c) siguiendo las reglas de la capa.

3. **Validador**: Extender `LayerSeparationValidator` para verificar que
   archivos en `_middleware/` no accedan a HAL ni hard.

4. **SDK Scanner**: Extender `SdkScanner` para enumerar componentes middleware
   existentes en `_middleware/`.

5. **Templates**: Crear `MiddlewareTemplate` con plantillas base para cada
   tipo de componente (filtro, detector, cola, conversor).

6. **architecture.md**: Actualizar diagrama de capas y agregar seccion
   middleware.

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

## 10. Glosario

| Termino | Definicion |
|---------|-----------|
| **Middleware** | Bloque de procesamiento intermedio entre driver y API |
| **inputFn** | Funcion de lectura que provee datos crudos (del driver o otra capa) |
| **outputFn** | Funcion de escritura que recibe datos procesados (de API o evento) |
| **Pipeline** | Cadena de middlewares conectados en serie |
| **getOutput()** | Funcion publica que retorna el ultimo valor procesado (pull) |
| **Wiring** | Conexion entre entrada/salida del middleware y funciones externas |
| **misMacros3** | Diccionario de macros de 3 niveles del compilador EMIC (col1.col2.key) |
| **Zero overhead** | Variantes A/B/D resuelven conexiones en compile-time, sin punteros |
| **Multi-instancia** | Soporte para N instancias independientes via parametro name= |
