# Propuesta: Reorganizacion de `_system/` y Reglas de la Capa `_middleware/`

> Propuesta para redefinir la capa `_system/` del nuevo SDK EMIC, separando
> conceptos mezclados en el SDK actual, y establecer reglas claras de acceso
> entre capas. Incluye la relacion con la nueva capa `_middleware/`.
>
> **Basado en**: Analisis profundo de los archivos actuales en `_system/` y
> sus consumidores reales en el SDK PIC_XC16.

---

## Indice

1. [Diagnostico del Estado Actual](#1-diagnostico-del-estado-actual)
2. [Problemas Identificados](#2-problemas-identificados)
3. [Propuesta: Nueva Estructura de `_system/`](#3-propuesta-nueva-estructura-de-_system)
4. [Reglas de Acceso por Capa (M4 Actualizado)](#4-reglas-de-acceso-por-capa-m4-actualizado)
5. [Resolucion del Conflicto `_hard/` + Streams](#5-resolucion-del-conflicto-_hard--streams)
6. [Capa `_middleware/` — Posicion y Reglas](#6-capa-_middleware--posicion-y-reglas)
7. [Diagrama de Dependencias Completo](#7-diagrama-de-dependencias-completo)
8. [Guia de Migracion](#8-guia-de-migracion)
9. [Checklist de Validacion](#9-checklist-de-validacion)

---

## 1. Diagnostico del Estado Actual

### Archivos en `_system/` (SDK PIC_XC16 actual)

```
_system/
├── systemInclusions.emic          # Orquestador: copia conversionFunctions.h a TARGET
├── inc/
│   └── conversionFunctions.h      # Macros de conversion tipo→tipo (snprintf/atof/atoi)
├── src/
│   └── conversionFunctions.c      # Comentado/no compilado (inactivo)
└── Stream/
    ├── streamIn.emic              # Orquestador: copia streamIn.h/.c a TARGET
    ├── streamOut.emic             # Orquestador: copia streamOut.h a TARGET
    ├── stream.emic                # Orquestador: frame FIFO (DEPRECADO)
    ├── inc/
    │   ├── streamIn.h             # typedef streamIn_t + conversiones desde stream
    │   ├── streamOut.h            # typedef streamOut_t
    │   └── stream.h               # typedef stream_t (frame FIFO, DEPRECADO)
    └── src/
        ├── streamIn.c             # Implementacion: conversiones + sendDataToStream()
        └── stream.c               # Implementacion frame FIFO (DEPRECADO)
```

### Consumidores reales (analisis del SDK PIC_XC16)

| Consumidor | Capa | Que usa | Como |
|-----------|------|---------|------|
| RS232, USB, BLE, WiFi, TCP, UDP, Telnet, MQTT, WebSocket | `_api/` | streamIn/streamOut | `EMIC:setInput(DEV:_system/Stream/streamIn.emic)` |
| UART1..UART5 | `_hard/` | streamIn/streamOut | `#include "streamIn.h"` / `#include "streamOut.h"` |
| I2C driver | `_drivers/` | streamIn | `EMIC:setInput(DEV:_system/Stream/streamIn.emic)` |
| (ninguno) | — | conversionFunctions | Solo incluido via `systemInclusions.emic` por el compilador EMIC |

### Hallazgos clave

1. **conversionFunctions** — Macros como `float_to_ascii()`, `ato_uint16_t()`,
   `uint8_t_to_ascii()`. Son herramientas del **parser EMIC** para resolver
   conversiones de tipo durante la compilacion. Ninguna capa del SDK las llama
   directamente. El unico consumidor es `streamIn.c`, que usa las macros
   `ato_*` internamente para implementar `streamIn_t_ptr_to_float()` etc.

2. **streamIn/streamOut** — Tipos fundamentales (`streamIn_t`, `streamOut_t`)
   que definen interfaces de flujo de datos con function pointers. Son la
   infraestructura runtime del SDK, usados por APIs de comunicacion y por
   las implementaciones UART en `_hard/`.

3. **stream** (frame FIFO) — **DEPRECADO**. Fue una version avanzada con
   framing que nunca se uso en produccion. Debe eliminarse del nuevo SDK.

4. **sendDataToStream** — Funcion printf-like con format specifiers propios
   de EMIC (`$s` = char\*, `$r` = streamIn_t\*). Vive dentro de `streamIn.c`.
   Es infraestructura runtime legitima.

---

## 2. Problemas Identificados

### P1. Mezcla de conceptos en `_system/`

`_system/` mezcla dos cosas fundamentalmente distintas:

| Concepto | Ejemplo | Naturaleza | Quienes lo usan |
|----------|---------|-----------|-----------------|
| **Herramientas del compilador EMIC** | `conversionFunctions.h` | Compile-time, parser interno | Solo el compilador EMIC |
| **Infraestructura runtime del SDK** | `streamIn.h`, `streamOut.h` | Runtime, tipos + funciones | `_api/`, `_drivers/`, `_hard/` |

Mezclar herramientas del compilador con infraestructura runtime genera
confusion sobre que es consumible y por quien.

### P2. Violacion de M4 por `_hard/`

El mandato M4 establece:

> `_hard/` NUNCA importa de `_api/`, `_drivers/`, `_middleware/` ni `_system/`.

Sin embargo, en el SDK actual TODAS las implementaciones UART en `_hard/`
hacen `#include "streamIn.h"` y `#include "streamOut.h"`. Esto es una
**violacion directa de M4**.

La razon: el UART en `_hard/` crea e inicializa structs `streamIn_t` /
`streamOut_t` con function pointers que apuntan a las funciones de lectura
y escritura del puerto, y los expone como `extern`. La API de comunicacion
los consume para enviar/recibir datos.

### P3. Codigo deprecado sin marcar

`stream.h/.c` (frame FIFO) esta deprecated pero sigue presente en la
estructura sin ninguna marca que indique su estado. Un DevAgent o
desarrollador nuevo podria intentar usarlo.

### P4. `_middleware/` no existe aun

La capa `_middleware/` esta definida en documentos de diseno pero no tiene
implementacion real. Su relacion con `_system/` (que tambien ofrece
"utilidades" y "procesamiento") necesita limites claros.

---

## 3. Propuesta: Nueva Estructura de `_system/`

### Principio rector

> `_system/` contiene **infraestructura runtime compartida** del SDK:
> tipos fundamentales, abstracciones de I/O y utilidades que multiples
> capas necesitan pero que no pertenecen a ninguna capa especifica.

### Que PERTENECE a `_system/`

```
_system/
├── systemInclusions.emic          # Orquestador principal
├── Stream/
│   ├── streamIn.emic              # Orquestador streamIn
│   ├── streamOut.emic             # Orquestador streamOut
│   ├── inc/
│   │   ├── streamIn.h             # typedef streamIn_t + conversiones
│   │   └── streamOut.h            # typedef streamOut_t
│   └── src/
│       └── streamIn.c             # Implementacion conversiones + sendDataToStream
└── Conversions/                   # (si se necesitan conversiones runtime futuras)
```

**Criterio de inclusion**: Un archivo pertenece a `_system/` si:
1. Define un **tipo o abstraccion fundamental** usada por 2+ capas
2. Es **C99 Freestanding puro** (no depende de hardware ni vendor)
3. No tiene logica de negocio — es infraestructura "neutral"
4. No es una herramienta del compilador/parser EMIC

### Que NO pertenece a `_system/`

| Componente | Estado actual | Destino en nuevo SDK | Razon |
|-----------|---------------|---------------------|-------|
| `conversionFunctions.h` | En `_system/inc/` | **Fuera del SDK** — infraestructura del compilador EMIC | Es una herramienta del parser, no codigo runtime. Las macros `float_to_ascii()`, `ato_uint16_t()` son usadas por el compilador para generar conversiones en compile-time. Ninguna capa las llama. |
| `conversionFunctions.c` | En `_system/src/` (inactivo) | **Eliminado** | Ya esta comentado/inactivo. No se compila. |
| `stream.h/.c` (frame FIFO) | En `_system/Stream/` | **Eliminado** | Deprecado, nunca usado en produccion. |
| Filtros, detectores, colas | No existe aun | `_middleware/` | Procesamiento intermedio va en `_middleware/`, no en `_system/` |

### conversionFunctions — Analisis detallado

Las funciones de conversion (`float_to_ascii`, `ato_uint16_t`, etc.) son
macros que envuelven `snprintf()` y `atof()`/`atoi()`. Su proposito es
que el compilador EMIC pueda generar codigo de conversion cuando el
integrador conecta tipos incompatibles en el editor visual.

**Observacion critica**: `streamIn.c` usa internamente las macros `ato_*`
de `conversionFunctions.h` para implementar funciones como
`streamIn_t_ptr_to_float()`. Esto crea una dependencia interna.

**Resolucion**: En el nuevo SDK, las funciones de conversion de stream
(`streamIn_t_ptr_to_float()`, `streamIn_t_ptr_to_uint16_t()`, etc.) deben
tener sus conversiones **inline en `streamIn.c`** sin depender de un
header externo de "conversionFunctions". Esto es posible porque las
conversiones son triviales (`atof()`, `atoi()`, etc.) y no justifican
un modulo separado.

Si en el futuro el compilador EMIC necesita macros de conversion para
generar codigo, esas macros son parte de la **infraestructura del compilador**
(similar a los templates de generacion), no del SDK runtime.

---

## 4. Reglas de Acceso por Capa (M4 Actualizado)

### Tabla de acceso completa

| Capa | Puede acceder a | NO puede acceder a |
|------|-----------------|-------------------|
| `_api/` | `_hal/`, `_drivers/`, `_middleware/`, `_system/` | `_hard/` |
| `_drivers/` | `_hal/`, `_system/` | `_hard/`, `_api/`, `_middleware/` |
| `_middleware/` | `_system/` | `_hard/`, `_hal/`, `_api/` (\*) |
| `_hal/` | `_hard/` (via routing) | `_api/`, `_drivers/`, `_system/` |
| `_hard/` | Headers vendor + C99 standard | `_api/`, `_drivers/`, `_hal/`, `_system/`, `_middleware/` |
| `_system/` | C99 standard headers solamente | Todas las demas capas |
| `_pcb/` | `_hard/` (define macros) | Todo lo demas |

(\*) `_middleware/` recibe funciones de `_api/` y `_drivers/` como
**parametros inyectados** (function pointers pasados en compile-time),
no importa archivos de esas capas directamente.

### Reglas explicitas

**R1. `_system/` es la base pasiva**
- `_system/` no importa ni depende de ninguna otra capa del SDK
- Solo usa C99 standard headers (`<stdint.h>`, `<stdbool.h>`, `<stddef.h>`,
  `<stdarg.h>`, `<string.h>`)
- Provee tipos y utilidades, nunca logica de negocio
- No registra inits, polls, ni publica recursos Discovery

**R2. `_hard/` es autosuficiente**
- `_hard/` solo accede a headers del vendor y C99 standard
- NO incluye headers de `_system/` (ni stream types, ni conversiones, ni nada)
- Expone funciones con signatures C99 puras
- En el modelo V2 callback, entrega datos via callbacks — no via structs
  de stream

**R3. `_system/` es accesible por las 3 capas portables**
- `_api/`, `_drivers/`, `_middleware/` pueden incluir archivos de `_system/`
- El mecanismo es `EMIC:setInput(DEV:_system/...)` en el archivo `.emic`
- Los tipos de `_system/` (ej: `streamIn_t`) son la **lingua franca** entre
  capas portables

**R4. `_middleware/` no importa archivos de `_api/` ni `_drivers/`**
- Las funciones de entrada/salida se inyectan como parametros (`input=`, `output=`)
- `_middleware/` conoce las **signatures** (via `_system/` types) pero no
  las **implementaciones**
- Esto permite que un middleware sea reutilizable con cualquier API o driver

**R5. No se mezclan herramientas del compilador con codigo runtime**
- Todo lo que es infraestructura del parser/compilador EMIC va fuera del SDK
- `_system/` solo contiene codigo que efectivamente se compila y ejecuta
  en el MCU target

---

## 5. Resolucion del Conflicto `_hard/` + Streams

### El problema

En el SDK actual, las implementaciones UART en `_hard/` crean e inicializan
structs `streamIn_t` / `streamOut_t`:

```c
// _hard/PIC24FJXXXGA306/UART/UART1.c (SDK actual — VIOLA M4)
#include "streamIn.h"
#include "streamOut.h"

streamIn_t  RS232_1_streamIn;    // extern, consumido por _api/
streamOut_t RS232_1_streamOut;   // extern, consumido por _api/

void UART1_init(void) {
    RS232_1_streamIn.get   = UART1_readByte;
    RS232_1_streamIn.count = UART1_dataAvailable;
    RS232_1_streamOut.put  = UART1_sendByte;
    // ...
}
```

Esto viola M4 porque `_hard/` incluye headers de `_system/`.

### La solucion: Modelo V2 — Streams construidos en la capa API

En el nuevo SDK, `_hard/` **NO conoce** los tipos stream. La capa API
construye los stream objects usando las funciones primitivas que `_hard/`
expone:

```
                    SDK Actual (V1)                 Nuevo SDK (V2)
                    ──────────────                  ──────────────
  _hard/ UART:      crea streamIn_t,                expone funciones
                    inicializa function ptrs,        primitivas solamente:
                    expone como extern               init, sendByte,
                                                     readByte, dataAvailable
                    #include "streamIn.h" ← VIOLA M4
                                                     NO #include de _system/

  _api/ RS232:      consume streamIn_t              construye streamIn_t
                    del extern de _hard/             usando las primitivas
                                                     de _hard/ (via HAL/driver)
                    #include "streamIn.h"            #include "streamIn.h"
```

### Ejemplo concreto: UART en el nuevo SDK

**`_hard/` — Solo primitivas C99 puras**:
```c
// _hard/{vendor}/{family}/{model}/UART/uart1.h
void     UART1_init(uint32_t baudrate);
void     UART1_sendByte(uint8_t data);
uint8_t  UART1_readByte(void);
uint8_t  UART1_dataAvailable(void);

// Callback V2: registrado por la capa superior
void     UART1_setRxCallback(void (*callback)(uint8_t data));
```

**`_api/` — Construye el stream**:
```c
// _api/Communication/RS232/src/RS232.c
#include "streamIn.h"
#include "streamOut.h"

streamIn_t  RS232_{name}_streamIn;
streamOut_t RS232_{name}_streamOut;

void RS232_{name}_init(void) {
    // Llamar init del HAL/driver inyectado
    .{driver}._init();

    // Construir stream objects con las primitivas
    RS232_{name}_streamIn.get   = .{driver}._readByte;
    RS232_{name}_streamIn.count = .{driver}._dataAvailable;
    RS232_{name}_streamOut.put  = .{driver}._sendByte;
}
```

### Beneficios del modelo V2

1. **M4 se cumple estrictamente**: `_hard/` no importa nada de `_system/`
2. **Separacion limpia**: `_hard/` expone primitivas, `_api/` las compone
3. **Flexibilidad**: La misma API RS232 puede construir su stream con
   primitivas de UART (via HAL), Bluetooth (via driver), USB CDC (via driver),
   etc. — todas exponen las mismas primitivas `init/sendByte/readByte/dataAvailable`
4. **Testabilidad**: Las primitivas de `_hard/` se pueden testear sin
   depender de stream types

---

## 6. Capa `_middleware/` — Posicion y Reglas

### Definicion

`_middleware/` contiene **bloques de procesamiento intermedios** reutilizables:
filtros, detectores, colas, conversiones de unidades, y cualquier logica de
procesamiento que actue entre una fuente de datos y un consumidor.

### Diferencia clara con `_system/`

| Aspecto | `_system/` | `_middleware/` |
|---------|-----------|----------------|
| **Proposito** | Infraestructura de tipos y abstracciones | Bloques de procesamiento con logica |
| **Contiene** | Tipos fundamentales (`streamIn_t`, etc.) | Filtros, detectores, colas, conversores |
| **Estado** | Stateless (solo definiciones) | Stateful (mantiene historial, buffers) |
| **Instanciable** | No (son tipos, no componentes) | Si (multiples filtros con distintos params) |
| **Discovery** | No publica recursos | Si publica funciones/variables/eventos |
| **Init/Poll** | No registra | Si registra (via la API que lo consume) |
| **Ejemplo** | `streamIn_t` define la interfaz de un flujo de entrada | `MovingAverage` filtra N muestras de un flujo |
| **Metafora** | "El lenguaje comun" | "Los bloques Lego" |

### Reglas de `_middleware/`

**MW1. Sin acceso a hardware**
- `_middleware/` NO accede a `_hal/` ni `_hard/`
- Las funciones de hardware llegan como parametros inyectados

**MW2. Entradas y salidas via parametros inyectados**
- La funcion de lectura (entrada) se inyecta via parametro `input=`
- La funcion de escritura/evento (salida) se inyecta via parametro `output=`
- El middleware solo conoce las **signatures** de estas funciones

**MW3. Puede usar `_system/`**
- Middleware puede incluir tipos de `_system/` (ej: `streamIn_t`)
  si necesita operar sobre streams
- Esto permite que un middleware trabaje con cualquier stream,
  independientemente de su origen

**MW4. Publicacion Discovery obligatoria**
- Todo middleware declara `EMIC:json(type = middleware)` con metadata
- El integrador selecciona, instancia y conecta middleware desde el editor

**MW5. Multi-instancia por defecto**
- Cada middleware acepta parametro `name=` para instanciacion multiple
- Ejemplo: dos filtros promedio movil con distinta ventana

**MW6. C99 Freestanding**
- Codigo portable, sin dependencias vendor ni toolchain-specific

### Categorias de `_middleware/`

```
_middleware/
├── Filters/                       # Filtros digitales
│   ├── MovingAverage/             # Promedio movil
│   ├── ExponentialSmoothing/      # Suavizado exponencial
│   └── MedianFilter/             # Filtro de mediana
├── Detectors/                     # Detectores de eventos
│   ├── ThresholdDetector/         # Detector de umbral
│   ├── ChangeDetector/            # Detector de cambio
│   └── HysteresisDetector/       # Detector con histeresis
├── Queues/                        # Colas y buffers
│   ├── CircularBuffer/            # Buffer circular
│   └── FIFO/                      # Cola FIFO
└── Converters/                    # Conversores de unidades
    ├── TemperatureConverter/       # C↔F↔K
    └── ScaleMapper/               # Mapeo lineal (ej: ADC → mV)
```

### Ejemplo: Flujo de datos con middleware

```
  MODULO (generate.emic)
  │
  │  1. Instancia driver:
  │     EMIC:setInput(DEV:_drivers/Sensor/LM35/LM35.emic, port=ADC1, name=lm35)
  │
  │  2. Instancia middleware:
  │     EMIC:setInput(DEV:_middleware/Filters/MovingAverage/MovingAverage.emic,
  │                   name=tempFilter, input=lm35_readRaw, window=8)
  │
  │  3. Instancia API:
  │     EMIC:setInput(DEV:_api/Sensors/Temperature/Temperature.emic,
  │                   driver=lm35, filter=tempFilter)
  │
  │  Resultado en runtime:
  │
  │     LM35 → readRaw() → MovingAverage(8) → Temperature API → evento etNewTemp
  │     ADC     driver        middleware          API               integrador
```

---

## 7. Diagrama de Dependencias Completo

### Diagrama de capas (nuevo SDK)

```
┌─────────────────────────────────────────────────────────┐
│  MODULO  (generate.emic + program.xml)                  │
│  Logica de negocio, configuracion, proyecto del usuario │
│  INSTANCIA: drivers, middleware, APIs                   │
├─────────────────────────────────────────────────────────┤
│  API  (_api/)                                           │
│  Abstraccion funcional: funciones, variables, eventos   │
│  Registra inits y polls. Construye streams.             │
│  Accede a: _hal/, _drivers/, _middleware/, _system/     │
├─────────────────────────────────────────────────────────┤
│  MIDDLEWARE  (_middleware/)                              │
│  Bloques de procesamiento: filtros, detectores, colas   │
│  Recibe I/O como parametros inyectados                  │
│  Accede a: _system/ (tipos). NO a _hal/ ni _hard/       │
├─────────────────────────────────────────────────────────┤
│  DRIVER  (_drivers/)                                    │
│  Control de hardware EXTERNO (chips, sensores, etc.)    │
│  Accede a: _hal/, _system/. NO a _hard/ ni _api/        │
├─────────────────────────────────────────────────────────┤
│  HAL  (_hal/)                                           │
│  Abstraccion de perifericos INTERNOS del MCU            │
│  Metadata + routing a _hard/. NO contiene codigo C.     │
├─────────────────────────────────────────────────────────┤
│  HARD  (_hard/{vendor}/{family}/{model}/)               │
│  Codigo especifico del microcontrolador                 │
│  Solo vendor headers + C99 standard. NADA mas.          │
├─────────────────────────────────────────────────────────┤
│  SYSTEM  (_system/)                                     │
│  Infraestructura runtime: tipos stream, utilidades      │
│  C99 Freestanding puro. Sin dependencias.               │
│  Accesible por: _api/, _drivers/, _middleware/           │
│  NO accesible por: _hard/, _hal/                        │
└─────────────────────────────────────────────────────────┘
```

### Grafo de dependencias (quien importa a quien)

```
                       ┌──────────┐
                       │  MODULO  │
                       └────┬─────┘
                            │ instancia (EMIC:setInput)
              ┌─────────────┼─────────────┐
              │             │             │
         ┌────▼────┐   ┌───▼────┐   ┌────▼────────┐
         │  _api/  │   │ _api/  │   │ _middleware/ │
         └────┬────┘   └───┬────┘   └──────┬───────┘
              │            │               │
         ┌────▼────┐  ┌───▼─────┐          │ (parametros)
         │_drivers/│  │  _hal/  │          │
         └────┬────┘  └───┬─────┘    ┌─────▼──────┐
              │           │          │  _system/   │
              └─────┬─────┘          │ (tipos)     │
                    │                └──────▲──────┘
               ┌────▼────┐                 │
               │ _hard/  │      ┌──────────┤
               └─────────┘      │          │
                           _api/ ──────────┘
                           _drivers/ ──────┘
```

### Matriz de acceso completa

```
              │ _api │ _drivers │ _middleware │ _hal │ _hard │ _system │
──────────────┼──────┼──────────┼────────────┼──────┼───────┼─────────┤
_api/         │  —   │   ✓ (*)  │     ✓      │  ✓   │  ✗    │    ✓    │
_drivers/     │  ✗   │    —     │     ✗      │  ✓   │  ✗    │    ✓    │
_middleware/  │ ✗(**) │  ✗(**)  │     —      │  ✗   │  ✗    │    ✓    │
_hal/         │  ✗   │    ✗     │     ✗      │  —   │  ✓    │    ✗    │
_hard/        │  ✗   │    ✗     │     ✗      │  ✗   │  —    │    ✗    │
_system/      │  ✗   │    ✗     │     ✗      │  ✗   │  ✗    │    —    │
```

(\*) `_api/` accede a `_drivers/` via inyeccion de dependencias (parametro `driver=`)
(\*\*) `_middleware/` recibe funciones de `_api/` y `_drivers/` como parametros
inyectados, no importa sus archivos directamente

---

## 8. Guia de Migracion

### Paso 1: Eliminar `conversionFunctions` del SDK

**Accion**: No incluir `conversionFunctions.h/.c` en el nuevo SDK.

**Impacto**: `streamIn.c` usa macros `ato_float()`, `ato_uint16_t()`, etc.

**Resolucion**: Reemplazar las macros con llamadas directas:
```c
// Antes (depende de conversionFunctions.h):
float streamIn_t_ptr_to_float(streamIn_t* s) {
    char buf[32]; /* ... leer del stream ... */
    return ato_float(buf);   // macro de conversionFunctions.h
}

// Despues (inline, sin dependencia):
float streamIn_t_ptr_to_float(streamIn_t* s) {
    char buf[32]; /* ... leer del stream ... */
    return (float)atof(buf);  // C99 standard <stdlib.h>
}
```

### Paso 2: Eliminar `stream` (frame FIFO)

**Accion**: No incluir `stream.h/.c` ni `stream.emic` en el nuevo SDK.

**Impacto**: Ninguno — el usuario confirmo que nunca se uso.

### Paso 3: Mover la creacion de streams de `_hard/` a `_api/`

**Accion**: Las implementaciones de UART en `_hard/` ya NO crean ni
inicializan `streamIn_t`/`streamOut_t`. Solo exponen funciones primitivas.

**Impacto**: Las APIs de comunicacion (RS232, USB, etc.) ahora construyen
los stream objects en su `init()` usando las primitivas inyectadas.

**Patron**:
```c
// En la API (ej: RS232.c):
void RS232_{name}_init(void) {
    .{driver}._init();   // init del HAL o driver inyectado

    RS232_{name}_streamIn.get   = .{driver}._readByte;
    RS232_{name}_streamIn.count = .{driver}._dataAvailable;

    RS232_{name}_streamOut.put  = .{driver}._sendByte;
    RS232_{name}_streamOut.getAvailable = .{driver}._txBufferAvailable;
}
```

### Paso 4: Crear estructura `_middleware/`

**Accion**: Crear la jerarquia de carpetas segun las categorias definidas
en la seccion 6. Comenzar con 2-3 middleware esenciales (ej: MovingAverage,
ThresholdDetector, CircularBuffer) como referencia para futuros desarrollos.

### Resumen de cambios

| Componente | SDK Actual | Nuevo SDK | Motivo |
|-----------|-----------|-----------|--------|
| `conversionFunctions.h` | `_system/inc/` | Eliminado del SDK | Es herramienta del parser, no runtime |
| `conversionFunctions.c` | `_system/src/` (inactivo) | Eliminado | Ya esta inactivo |
| `stream.h/.c/.emic` | `_system/Stream/` | Eliminado | Deprecado |
| `streamIn.h/.c/.emic` | `_system/Stream/` | `_system/Stream/` | Se mantiene, se elimina dependencia de conversionFunctions |
| `streamOut.h/.emic` | `_system/Stream/` | `_system/Stream/` | Se mantiene sin cambios |
| Stream creation en UART | `_hard/` | `_api/` | Cumplir M4 |
| Filtros, detectores, etc. | Embebidos en `_api/` | `_middleware/` | Separacion de responsabilidades |

---

## 9. Checklist de Validacion

### Para `_system/`

- [ ] `_system/` solo contiene tipos y utilidades runtime (NO herramientas del compilador)
- [ ] `_system/` no incluye headers de ninguna otra capa del SDK
- [ ] `_system/` solo usa C99 standard headers
- [ ] `conversionFunctions` no existe en el nuevo SDK
- [ ] `stream` (frame FIFO) no existe en el nuevo SDK
- [ ] `streamIn.c` no depende de `conversionFunctions.h`
- [ ] `_hard/` no hace `#include` de ningun header de `_system/`
- [ ] `_hard/` no crea ni inicializa structs `streamIn_t` / `streamOut_t`

### Para `_middleware/`

- [ ] Cada middleware tiene `EMIC:json(type = middleware)` con metadata
- [ ] Ningun middleware incluye headers de `_hal/` o `_hard/`
- [ ] Las entradas/salidas se reciben como parametros, no como imports
- [ ] Cada middleware es multi-instancia (acepta `name=`)
- [ ] Cada middleware es C99 Freestanding
- [ ] Los filtros/procesamiento NO estan embebidos en APIs

### Para la coherencia inter-capas

- [ ] Solo `_api/` registra `inits.*` y `polls.*`
- [ ] `_api/` construye stream objects en su init (no delega a `_hard/`)
- [ ] `_drivers/` puede usar `_system/` para tipos stream
- [ ] `_middleware/` puede usar `_system/` para tipos stream
- [ ] La matriz de acceso M4 se respeta sin excepciones

---

## Glosario

| Termino | Definicion |
|---------|-----------|
| **`_system/`** | Capa base del SDK que contiene infraestructura runtime compartida: tipos fundamentales y utilidades que multiples capas necesitan. C99 Freestanding puro. |
| **`_middleware/`** | Capa de bloques de procesamiento intermedios reutilizables: filtros, detectores, colas, conversores. Conectables via parametros inyectados. |
| **streamIn_t** | Struct con function pointers (`get`, `count`) que define la interfaz de un flujo de datos de entrada. Definido en `_system/`. |
| **streamOut_t** | Struct con function pointers (`put`, `getAvailable`) que define la interfaz de un flujo de datos de salida. Definido en `_system/`. |
| **Modelo V2** | Arquitectura donde `_hard/` entrega datos via callbacks y expone primitivas C99. No crea stream objects — eso lo hace `_api/`. |
| **Inyeccion de parametros** | Patron donde las funciones de I/O se pasan como parametros al middleware/API en compile-time, evitando acoplamientos directos entre capas. |
| **Infraestructura runtime** | Codigo que se compila y ejecuta en el MCU target (vs herramientas del compilador que solo existen en el host). |
| **conversionFunctions** | Macros de conversion de tipos (`float_to_ascii`, etc.) que eran herramientas del parser EMIC. **Eliminadas del nuevo SDK**. |
