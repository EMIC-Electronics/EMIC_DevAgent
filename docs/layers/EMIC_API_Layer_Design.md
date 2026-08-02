# Diseno de la Capa `_api/`: APIs de Alto Nivel del SDK EMIC

> Documento de diseno detallado para la capa `_api/` del SDK EMIC.
> Define la estructura interna, patrones arquitectonicos, convenciones
> de archivos y guias para crear nuevas APIs.
>
> **Documento complementario de**: `EMIC_HAL_Hard_Redesign_Proposal.md`
> (que define la arquitectura general HAL+Hard y los Mandatos M1-M4).
>

---

## Mandatos Aplicables (referencia)

Este documento se rige por los cuatro mandatos definidos en
`EMIC_HAL_Hard_Redesign_Proposal.md`. Se resumen aqui como referencia
rapida — la version completa y normativa esta en el documento padre.

| Mandato | Aplicacion en `_api/` |
|---------|----------------------|
| **M1** (C99 Freestanding + toolchain nativo) | `_api/` genera codigo C99 Freestanding puro. No usa headers vendor, asm, ni pragmas. Todo acceso a hardware se delega a `_drivers/` → `_hal/` → `_hard/`. |
| **M2** (Escalabilidad) | Agregar una API = crear carpeta + archivos. Ninguna API existente se modifica. |
| **M3** (AI-first) | Cada API declara sus recursos con comentarios Discovery (Doxygen + tags `@fn`, `@var`, `@alias`). Los configuradores usan `EMIC:json(type=configurator)`. |
| **M4** (Separacion por capas) | `_api/` puede acceder a `_hal/`, `_drivers/`, `_middleware/` y `_system/`. NUNCA accede a `_hard/`. `_drivers/` es exclusivamente para hardware externo al MCU; `_hal/` es la abstraccion suficiente para perifericos internos. |

---

## Indice

0. [Mandatos Aplicables](#mandatos-aplicables-referencia)
1. [Rol de la Capa `_api/`](#1-rol-de-la-capa-_api)
2. [Ubicacion en la Arquitectura de Capas](#2-ubicacion-en-la-arquitectura-de-capas)
3. [Estructura de Directorios](#3-estructura-de-directorios)
4. [Anatomia de una API](#4-anatomia-de-una-api)
5. [Patrones Arquitectonicos](#5-patrones-arquitectonicos)
6. [Discovery: Publicacion de Recursos](#6-discovery-publicacion-de-recursos)
7. [Registro y Compilacion](#7-registro-y-compilacion)
8. [Ejemplos Completos por Tipo de API](#8-ejemplos-completos-por-tipo-de-api)
9. [Cadena de Dependencias — Ejemplo Completo](#9-cadena-de-dependencias--ejemplo-completo)
10. [Reglas para Crear Nuevas APIs](#10-reglas-para-crear-nuevas-apis)
11. [Impacto en Capas Adyacentes](#11-impacto-en-capas-adyacentes)
12. [Validacion y Verificacion](#12-validacion-y-verificacion)
13. [Glosario](#13-glosario)

---

## 1. Rol de la Capa `_api/`

### Definicion

La capa `_api/` es la **capa de abstraccion de alto nivel** del SDK EMIC.
Expone funciones, variables y eventos que el integrador consume desde
`program.xml` en el editor EMIC. Se ubica entre el **modulo** (capa de
aplicacion) y los **drivers** (capa de hardware externo).

### Responsabilidades

```
┌──────────────────────────────────────────────────────────────────┐
│  _api/ — APIs de Alto Nivel                                      │
│                                                                  │
│  1. Abstraccion de hardware                                      │
│     → Oculta diferencias entre drivers con la misma funcion      │
│     → El modulo consume la API sin saber que driver hay debajo   │
│                                                                  │
│  2. Procesamiento intermedio                                     │
│     → Filtros digitales, maquinas de estados, conversiones       │
│     → Buffers circulares, protocolos de alto nivel               │
│     → Logica que no es del driver ni del modulo                  │
│                                                                  │
│  3. Publicacion de recursos                                      │
│     → Declara funciones, variables y eventos para Discovery      │
│     → El integrador los arrastra a program.xml en el editor      │
│                                                                  │
│  4. Registro de init/poll                                        │
│     → Unica capa que registra EMIC:define(inits.X / polls.X)    │
│     → Llama en cadena a inits/polls de capas inferiores          │
│                                                                  │
│  5. Consumo de dependencias                                      │
│     → Accede a HAL directamente (perifericos internos)           │
│     → Consume drivers inyectados por el modulo (HW externo)      │
│     → Incluye middleware y system libraries                       │
└──────────────────────────────────────────────────────────────────┘
```

### Principio fundamental

> La API es **independiente del modulo que la consume**. Multiples modulos
> pueden usar la misma API con distintos drivers y configuraciones. La API
> nunca contiene logica de negocio especifica de un proyecto.

### Que NO hace `_api/`

- **NO accede a `_hard/` directamente**: La API nunca toca SFRs,
  ISRs ni registros del vendor. Para perifericos internos del MCU
  accede via `_hal/`; para hardware externo, via `_drivers/`.
- **NO contiene logica de negocio**: Si algo es especifico de un proyecto,
  va en `program.xml` del modulo, no en la API.
- **NO implementa hardware externo**: El control de chips externos
  (sensores, transceivers, displays) va en `_drivers/`.
- **NO se ejecuta de forma bloqueante**: Todo poll debe retornar en
  microsegundos. Nunca `delay()` ni `while` bloqueante.

---

## 2. Ubicacion en la Arquitectura de Capas

```
┌─────────────────────────────────────────────────────────┐
│  MODULO  (generate.emic + program.xml)                  │
│  Logica de negocio, configuracion, proyecto del usuario │
├─────────────────────────────────────────────────────────┤
│  API  (_api/)                        ◄── ESTA CAPA     │
│  Abstraccion funcional: funciones, variables, eventos   │
│  Registra inits y polls. Consume HAL, drivers, middle.  │
├─────────────────────────────────────────────────────────┤
│  MIDDLEWARE  (_middleware/)                              │
│  Bloques de procesamiento: filtros, detectores, colas   │
├─────────────────────────────────────────────────────────┤
│  DRIVER  (_drivers/)                                    │
│  Control de hardware EXTERNO al MCU (chips, sensores)   │
│  Consume HAL para acceder a perifericos del MCU.        │
├─────────────────────────────────────────────────────────┤
│  HAL  (_hal/)                                           │
│  Abstraccion de perifericos internos del MCU            │
├─────────────────────────────────────────────────────────┤
│  HARD  (_hard/{mcuName}/)                               │
│  Codigo especifico del microcontrolador                 │
│  Registros, interrupciones, configuracion de pines      │
└─────────────────────────────────────────────────────────┘
```

**Regla de dependencia**: `_api/` NUNCA accede a `_hard/`. Para todo
lo demas, la API tiene acceso flexible segun el tipo de recurso.

### Frontera entre `_hal/` y `_drivers/`

La distincion clave es **periferico interno del MCU** vs **hardware externo**:

| Recurso | Capa | API accede via... | Ejemplo |
|---------|------|-------------------|---------|
| GPIO, Timer interno, ADC interno, UART, SPI, I2C, RTC interno | `_hal/` | `EMIC:setInput(DEV:_hal/...)` directo | LED usa GPIO via HAL |
| Sensor externo, transceiver, display, RTC externo, ADC externo | `_drivers/` | Inyeccion desde el modulo | LoadCell usa ADS1231 via driver |

- `_drivers/` es **exclusivamente para hardware externo** al MCU (chips con datasheet propio)
- `_hal/` es la **abstraccion suficiente** para perifericos internos del MCU
- No se necesita un "driver" intermedio para perifericos internos — HAL ya cumple ese rol

### Acceso a `_system/`

La capa `_api/` SI puede acceder a `_system/` (streams, conversiones,
includes comunes). A diferencia de `_hard/`, que NUNCA accede a `_system/`,
la API consume estas utilidades libremente:

```
EMIC:setInput(DEV:_system/Stream/stream.emic)
EMIC:setInput(DEV:_system/Stream/streamOut.emic)
EMIC:setInput(DEV:_system/Stream/streamIn.emic)
```

---

## 3. Estructura de Directorios

### Jerarquia: `{Categoria}/{NombreAPI}/`

```
_api/
├── Actuators/                          ← Reles, motores
│   └── Relay/
├── ADC/                                ← Conversion analogico-digital
│   └── ADC/
├── Audio/                              ← I2S audio
│   └── I2SAudio/
├── Indicators/                         ← LEDs, matrices LED
│   ├── LEDs/
│   └── LEDMatrix/
├── Inputs/                             ← Entradas digitales
│   └── DigitalInputs/
├── Protocols/                          ← Modbus, protocolos industriales
│   ├── Modbus/
│   └── DinaModbus/
├── Sensors/                            ← Sensores fisicos
│   ├── LoadCell/
│   ├── Temperature/
│   ├── ForceSensor/
│   ├── AnalogInput/
│   ├── Accelerometer/
│   └── GPS/
├── Storage/                            ← Flash, RAM externa
│   ├── Flash/
│   └── RAM/
├── System/                             ← Persistencia, RTCC
│   ├── Persist/
│   └── RTCC/
├── Timers/                             ← Temporizadores
│   └── timer_api/
├── Wired_Communication/                ← RS232, USB, EMICBus
│   ├── RS232/
│   ├── USB/
│   └── EMICBus/
└── Wireless/                           ← Bluetooth, LoRa, Radio
    ├── Bluetooth/
    ├── LoRa_WAN/
    └── RadioFSK/
```

### Estructura interna de cada API

```
_api/{Categoria}/{NombreAPI}/
├── {NombreAPI}.emic          # Orquestador: dependencias + copy/setInput
├── inc/
│   └── {NombreAPI}.h         # Interfaz: Discovery metadata, init/poll, eventos
└── src/
    └── {NombreAPI}.c         # Implementacion: init, poll, funciones, eventos
```

---

## 4. Anatomia de una API

### 4.1. Archivo `.emic` (Orquestador)

El archivo `.emic` es un script de metaprogramacion que:
- Declara el tag de la API para Discovery
- Publica comentarios Discovery (funciones, eventos, variables)
- Incluye dependencias directas (HAL para perifericos internos, `_system/`)
- Consume drivers inyectados por el modulo via parametro `driver=`
- Copia sus propios `.h` y `.c` al `TARGET:`
- Registra archivos para compilacion

**Estructura tipica:**

```
// 1. Tag de identificacion
EMIC:tag(driverName = NombreAPI)

// 2. Configuradores UI (opcional)
EMIC:json(type = configurator)
{ ... }

// 3. Comentarios Discovery (funciones, eventos, variables)
/**
* @fn void miFuncion(uint8_t param);
* @alias NombreAmigable
* @brief Descripcion para el integrador
* @param param Descripcion del parametro
* @return Nothing
*/

/**
* @fn extern void miEvento(int32_t dato);
* @alias EventoAmigable
* @brief Cuando ocurre tal condicion
*/

// 4. Dependencias directas (system, HAL para perifericos internos)
EMIC:setInput(DEV:_system/Stream/stream.emic)
EMIC:setInput(DEV:_hal/GPIO/gpio.emic)      // periferico interno → HAL directo
// Nota: los drivers externos son INYECTADOS por el modulo via
// parametro "driver=nombre", no incluidos directamente por la API

// 5. Copia de archivos propios al TARGET
EMIC:copy(inc/NombreAPI.h > TARGET:inc/NombreAPI.h, param=.{param}.)
EMIC:copy(src/NombreAPI.c > TARGET:NombreAPI.c, param=.{param}.)

// Nota: EMIC:copy(src > dst, params) es equivalente a:
//   EMIC:setOutput(dst)
//   EMIC:setInput(src, params)
//   EMIC:restoreOutput
// Usar setOutput/restoreOutput cuando hay multiples archivos de entrada
// o texto literal intercalado:
//   EMIC:setOutput(TARGET:inc/config.h)
//       EMIC:setInput(inc/clock_config.h, freq=.{freq}.)
//       EMIC:setInput(inc/pin_config.h, pcb=.{pcb}.)
//       #define BOARD_NAME ".{pcb}."
//   EMIC:restoreOutput

// 6. Registro para compilacion
EMIC:define(main_includes.NombreAPI, NombreAPI)
EMIC:define(c_modules.NombreAPI, NombreAPI)
```

### 4.2. Archivo `.h` (Interfaz + Discovery + Registros)

El header cumple multiples funciones en un solo archivo:

**a) Declaraciones de funciones con metadata Discovery:**
```c
/**
* @fn void start_ADC(uint8_t Freq, uint32_t Quantity);
* @alias StartADC
* @brief Inicia la conversion del ADC
* @param Freq Frecuencia de muestreo (1=max, 13=min)
* @param Quantity Cantidad de muestras
* @return Nothing
*/
void start_ADC(uint8_t Freq, uint32_t Quantity);
```

**b) Declaraciones de eventos (condicionales):**
```c
EMIC:ifdef usedEvent.eADC
/**
* @fn extern void eADC(int32_t Result);
* @alias DataReady
* @brief Dato del ADC listo
* @param Result Resultado de conversion
*/
extern void eADC(int32_t Result);
EMIC:endif
```

**c) Declaraciones de variables publicadas:**
```c
/**
* @var float Capacidad;
* @alias Capacidad
* @brief Peso maximo para el cual la celda es lineal
*/
extern float Capacidad;
```

**d) Registro de init/poll:**
```c
void init_ADC(void);
EMIC:define(inits.init_ADC, init_ADC)

void poll_ADC(void);
EMIC:define(polls.poll_ADC, poll_ADC)
```

**e) Compilacion condicional de funciones/polls:**
```c
EMIC:ifdef usedFunction.LEDs_led1_blink
void LEDs_led1_poll(void);
EMIC:define(polls.LEDs_led1, LEDs_led1_poll)
EMIC:endif
```

### 4.3. Archivo `.c` (Implementacion)

El source implementa:
- `init_*()` — Inicializacion de la API + inicializacion encadenada de capas inferiores
- `poll_*()` — Logica no-bloqueante ejecutada en cada ciclo del main loop
- Funciones publicadas (las que Discovery indexa)
- Invocacion condicional de eventos

**Patron de init encadenado:**
```c
void init_LoadCell(void) {
    .{driver}._init();    // → init del driver inyectado
    Balanza_flags = 0;
    for (int i = 0; i < HistoryLength; i++)
        Historial[i] = 0;
}
```

**Patron de poll con eventos condicionales:**
```c
void poll_LoadCell(void) {
    if ((Balanza_flags & F_Stable)) {
        if (!(Balanza_flags & F_StableEventTrigger)) {
            Balanza_flags |= F_StableEventTrigger;
            Balanza_flags &= ~F_UnstableEventTrigger;
            if (Peso_neto_f == 0) {
                EMIC:ifdef usedEvent.eZero
                eZero();
                EMIC:endif
            } else {
                EMIC:ifdef usedEvent.eStable
                eStable();
                EMIC:endif
            }
        }
    }
}
```

---

## 5. Patrones Arquitectonicos

### 5.1. Inyeccion de Dependencias (Driver Injection)

En el nuevo SDK, el **modulo** (que conoce el hardware completo) es quien
decide que driver o HAL utilizar. El modulo instancia el driver/HAL primero
y luego pasa una referencia a la API. La API no necesita conocer la ruta
del driver ni propagar parametros que no le son propios.

**Modelo de inyeccion:**

```
// generate.emic del modulo

// 1. Instanciar el driver (con parametros especificos del driver)
EMIC:setInput(DEV:_drivers/USB/MCP2200/MCP2200.emic,
              port=1, baud=9600, name=uart1)
  // → copia a TARGET:inc/uart1.h y TARGET:uart1.c
  // → expone funciones: uart1_init(), uart1_sendByte(), etc.

// 2. Invocar la API (solo parametros propios de la API + referencia al driver)
EMIC:setInput(DEV:_api/Wired_Communication/RS232/rs232.emic,
              driver=uart1, BufferSize=512)
  // → incluye inc/uart1.h
  // → usa uart1_init(), uart1_sendByte(), etc.
  // → BufferSize se consume aqui (buffer circular de la API)
```

**Ventajas sobre el modelo rigido (API construye ruta del driver):**
- Cada driver recibe solo los parametros que le corresponden
- La API no necesita propagar parametros que no entiende
- Drivers distintos con parametros distintos funcionan sin cambiar la API
- La API no sabe si hay un driver externo o un HAL interno debajo

**Contrato por convencion de nombres:**

El parametro `name=` del driver determina el prefijo de todas las funciones.
La API, al recibir `driver=uart1`, sabe que puede llamar a `.{driver}._init()`,
`.{driver}._sendByte()`, etc. El contrato es implicito en la convencion de
nombres — si el driver expone las funciones esperadas, es compatible.

```c
// En el .c de la API:
void RS232_Init(void) {
    .{driver}._init();     // → uart1_init()
}

void RS232_sendByte(uint8_t data) {
    .{driver}._sendByte(data);  // → uart1_sendByte()
}
```

**Intercambiabilidad — RTC externo vs interno:**

```
// Opcion A: RTC externo (DS3231 via I2C)
EMIC:setInput(DEV:_drivers/RTC/DS3231/DS3231.emic, port=I2C1, name=rtc)
EMIC:setInput(DEV:_api/System/RTCC/RTCC.emic, driver=rtc)

// Opcion B: RTC interno del MCU
EMIC:setInput(DEV:_hal/RTC/RTC.emic, name=rtc)
EMIC:setInput(DEV:_api/System/RTCC/RTCC.emic, driver=rtc)

// La API es IDENTICA en ambos casos — solo cambia la linea del driver
```

En ambos casos, el driver/HAL copia a `TARGET:inc/rtc.h` con las mismas
funciones (`rtc_init()`, `rtc_getTime()`, `rtc_setTime()`, etc.) y la API
las consume sin saber la implementacion.

### 5.2. Acceso Directo a HAL (Perifericos Internos)

Para perifericos internos del MCU, la API puede acceder a `_hal/`
directamente sin necesidad de inyeccion. El HAL ya provee la abstraccion
necesaria:

```
// En led.emic — acceso directo a HAL (periferico interno)
EMIC:setInput(DEV:_hal/GPIO/gpio.emic)

// En el .c de la API:
HAL_GPIO_PinCfg(.{pin}., GPIO_OUTPUT);
HAL_GPIO_PinSet(.{pin}., GPIO_HIGH);
```

Esto aplica a: GPIO, Timer interno, ADC interno, UART, SPI, I2C, RTC
interno, y cualquier otro periferico que venga dentro del silicon del MCU.

**Cuando usar cada modelo:**

| Escenario | Modelo | Ejemplo |
|-----------|--------|---------|
| Periferico interno simple | API → HAL directo | LED usa GPIO |
| Periferico interno con inyeccion | Modulo inyecta HAL | RTC interno inyectado como `rtc` |
| Hardware externo | Modulo inyecta driver | ADS1231 inyectado como `adc` |
| Hardware externo intercambiable | Modulo inyecta driver A o B | DS3231 o HAL/RTC, ambos como `rtc` |

### 5.2. Eventos Opt-in (Zero-Cost Abstraction)

Los eventos se declaran en la API pero solo se compilan si el integrador los
usa en `program.xml`. El mecanismo es `EMIC:ifdef usedEvent.{nombre}`:

```c
// Declaracion en .h (condicional)
EMIC:ifdef usedEvent.eTemperatureReady
extern void eTemperatureReady(float temperature);
EMIC:endif

// Invocacion en .c (condicional)
EMIC:ifdef usedEvent.eTemperatureReady
eTemperatureReady(current_temp);
EMIC:endif
```

**Resultado:** Si el integrador no implementa el evento, el codigo desaparece
completamente del binario — cero overhead en ROM y RAM.

El mismo mecanismo aplica a funciones con `EMIC:ifdef usedFunction.{nombre}`:

```c
EMIC:ifdef usedFunction.LEDs_led1_blink
void LEDs_led1_blink(uint16_t timeOn, uint16_t period, uint16_t times) {
    // ... implementacion del blink ...
}
EMIC:endif
```

### 5.3. Registro de Init/Poll

Las APIs registran sus funciones `init` y `poll` usando macros que el sistema
`main.emic` recolecta:

```c
// En el .h de la API
EMIC:define(inits.init_LoadCell, init_LoadCell)
EMIC:define(polls.poll_LoadCell, poll_LoadCell)
```

**Regla critica:** Solo la API registra inits/polls. Los drivers, HAL y hard
NO registran los suyos — son llamados en cadena desde la API:

```c
void init_LoadCell(void) {
    .{driver}._init();    // → init del driver inyectado
    // ... configuracion propia de la API ...
}

void poll_LoadCell(void) {
    // El driver usa interrupciones + callback, no necesita poll
    // ... logica de filtrado y deteccion de estabilidad ...
}
```

Si `_hard/` o `_drivers/` registraran sus propios inits/polls, se duplicarian
llamadas y se perderia el control del orden de ejecucion.

### 5.4. Intercambiabilidad de Drivers

La intercambiabilidad se logra mediante inyeccion (ver 5.1). El modulo
decide que driver usar; la API solo consume la interfaz inyectada:

```
// Modulo A: usa MCP2200
EMIC:setInput(DEV:_drivers/USB/MCP2200/MCP2200.emic, port=1, baud=9600, name=uart1)
EMIC:setInput(DEV:_api/Wired_Communication/RS232/rs232.emic, driver=uart1, BufferSize=512)

// Modulo B: usa FT232 (misma API, distinto driver)
EMIC:setInput(DEV:_drivers/USB/FT232/FT232.emic, port=2, baud=115200, name=uart1)
EMIC:setInput(DEV:_api/Wired_Communication/RS232/rs232.emic, driver=uart1, BufferSize=256)
```

Todos los drivers de una misma categoria exponen funciones con el mismo
contrato de nombres (ej: `{name}_init()`, `{name}_sendByte()`). Cambiar
de driver = cambiar UNA linea en `generate.emic`, la API no se toca.

### 5.5. Callback Inverso (Driver → API)

Algunos drivers definen funciones `extern` que la API debe implementar, invirtiendo
el flujo de control:

```c
// En el driver ADS1231.h:
extern void nuevaLectura(int32_t nuevo_valor);  // API must implement this

// En la API LoadCell.c:
void nuevaLectura(int32_t adcValue) {
    // Procesa lectura del ADC, actualiza filtro, detecta estabilidad
    Acumulador -= Historial[Indice];
    Acumulador += adcValue;
    Historial[Indice] = adcValue;
    // ...
}
```

Esto permite que el driver notifique a la API por interrupcion sin dependencia
circular. El driver declara el `extern`; la API provee la implementacion.

### 5.6. Instanciacion Multiple

Algunas APIs soportan multiples instancias usando un parametro `name=`:

```
EMIC:setInput(DEV:_api/Timers/timer_api.emic, name=1)
EMIC:setInput(DEV:_api/Timers/timer_api.emic, name=2)
```

El nombre se sustituye en todas las funciones, eventos y archivos:
- `setTime.{name}.` → `setTime1`, `setTime2`
- `etOut.{name}.` → `etOut1`, `etOut2`
- `timer_api.{name}..h` → `timer_api1.h`, `timer_api2.h`

Cada instancia tiene su propio archivo `.c` y `.h` con identificadores unicos,
y registra sus propios inits/polls independientes:

```c
// timer_api1.h
EMIC:define(inits.timer1, timer1_init)
EMIC:define(polls.timer1, timer1_Poll)

// timer_api2.h
EMIC:define(inits.timer2, timer2_init)
EMIC:define(polls.timer2, timer2_Poll)
```

### 5.7. Configurador UI (EMIC:json)

Las APIs pueden declarar opciones de configuracion que generan wizards en el
editor web:

```c
EMIC:json(type = configurator)
{
    "name": "FilterLength",
    "brief": "Configuracion del filtro de la celda de carga",
    "legend": "Seleccione la longitud del filtro",
    "options": [
        { "legend": "Corto (8 muestras)", "value": "8",
          "brief": "Respuesta rapida, menor filtrado" },
        { "legend": "Medio (32 muestras)", "value": "32",
          "brief": "Balance entre velocidad y filtrado" },
        { "legend": "Largo (64 muestras)", "value": "64",
          "brief": "Respuesta lenta, mejor filtrado" }
    ]
}
```

El valor seleccionado se inyecta como `.{config.FilterLength}.` durante la
compilacion y se puede usar en `.h` y `.c`:

```c
#define HistoryLength .{config.FilterLength}.
```

### 5.8. Ejecucion No-Bloqueante

Todas las APIs siguen un modelo de ejecucion cooperativo basado en polling:

```c
// main.c generado automaticamente
void main(void) {
    // Fase de inicializacion (una vez)
    .{inits.*}.     // → init_LoadCell(); init_Timer1(); init_USB(); ...

    while(1) {
        // Fase de polling (loop infinito)
        .{polls.*}.  // → poll_LoadCell(); timer1_Poll(); poll_USB(); ...
    }
}
```

**Reglas de poll:**
- Nunca usar `delay()` ni `while` bloqueante
- Usar flags y maquinas de estados para logica temporal
- Cada `poll` debe retornar rapidamente (microsegundos)
- Los eventos se disparan desde `poll` cuando se detecta la condicion

---

## 6. Discovery: Publicacion de Recursos

El proceso Discovery escanea los archivos `.h` de las APIs buscando
comentarios Doxygen con tags especiales:

| Tag | Tipo | Ejemplo |
|-----|------|---------|
| `@fn void func(...)` | Funcion | `@fn void start_ADC(uint8_t Freq, uint32_t Qty)` |
| `@fn extern void event(...)` | Evento | `@fn extern void eADC(int32_t Result)` |
| `@fn variadic func(...)` | Funcion variadic | `@fn variadic pRS232(char* format,...)` |
| `@var tipo nombre` | Variable | `@var float Capacidad` |
| `@alias` | Nombre amigable | `@alias DataReady` |
| `@brief` | Descripcion | `@brief Dato del ADC listo` |
| `@param` | Parametro | `@param Freq Frecuencia de muestreo` |
| `@return` | Retorno | `@return Nothing` |

### Mapeo a program.xml

Los recursos descubiertos se presentan al integrador en el editor EMIC, donde puede
arrastrarlos a `program.xml` para construir la logica de su aplicacion.

**Funciones** se convierten en bloques invocables:
```xml
<emic-function name="start_ADC">
    <emic-function-parameter type="uint8_t">
        <emic-literal-numerical value="1"/>
    </emic-function-parameter>
    <emic-function-parameter type="uint32_t">
        <emic-literal-numerical value="100"/>
    </emic-function-parameter>
</emic-function>
```

**Eventos** se convierten en handlers que el integrador implementa:
```xml
<emic-event name="eADC">
    <!-- Codigo del integrador aqui -->
</emic-event>
```

### Regla de tipos en program.xml

El `type` del parametro dicta el literal a usar:
- `type="char"` → `emic-literal-char` → genera `'T'`
- `type="char*"` → `emic-literal-string` → genera `"texto"`
- `type="uint8_t|uint16_t|..."` → `emic-literal-numerical` → genera `60000`
- `type="concat"` → multiples literals → genera format string con `$s`/`$r`

---

## 7. Registro y Compilacion

Cada API se registra con dos macros al final de su `.emic`:

```c
EMIC:define(main_includes.NombreAPI, NombreAPI)   // → #include "inc/NombreAPI.h"
EMIC:define(c_modules.NombreAPI, NombreAPI)        // → NombreAPI.c en el proyecto
```

El sistema de build recolecta:
- `main_includes.*` → genera los `#include` en `main.c`
- `c_modules.*` → genera la lista de archivos `.c` del proyecto MPLAB-X
- `inits.*` → genera llamadas de inicializacion: `.{inits.*}.` → `init_LoadCell(); init_Timer1(); ...`
- `polls.*` → genera llamadas de polling: `.{polls.*}.` → `poll_LoadCell(); timer1_Poll(); ...`

---

## 8. Ejemplos Completos por Tipo de API

### 8.1. API Simple — LED

Control de LEDs con patrones de parpadeo. Demuestra: parametrizacion
por nombre, compilacion condicional de poll, acceso directo a HAL
(GPIO es periferico interno del MCU).

**led.emic:**
```
EMIC:tag(driverName = LEDs)

/**
* @fn void LEDs_.{name}._state(uint8_t state);
* @alias .{name}..state
* @brief Change the state of the led, 1-on, 0-off, 2-toggle.
* @param state 1-on 0-off 2-toggle
* @return Nothing
*/

/**
* @fn void LEDs_.{name}._blink(uint16_t timeOn, uint16_t period, uint16_t times);
* @alias .{name}..blink
* @brief blink the .{name}.
* @param timeOn time in milliseconds led is on
* @param period period in milliseconds
* @param times number of blinks (0 = infinite)
* @return Nothing
*/

EMIC:setInput(DEV:_hal/GPIO/gpio.emic)
EMIC:setInput(DEV:_drivers/SystemTimer/systemTimer.emic)

EMIC:copy(inc/led.h > TARGET:inc/led_.{name}..h, name=.{name}., pin=.{pin}.)
EMIC:copy(src/led.c > TARGET:led_.{name}..c, name=.{name}., pin=.{pin}.)

EMIC:define(main_includes.led_.{name}., led_.{name}.)
EMIC:define(c_modules.led_.{name}., led_.{name}.)
```

**inc/led.h:**
```c
void LEDs_.{name}._init(void);
EMIC:define(inits.LEDs_.{name}., LEDs_.{name}._init)

EMIC:ifdef usedFunction.LEDs_.{name}._blink
void LEDs_.{name}._poll(void);
EMIC:define(polls.LEDs_.{name}., LEDs_.{name}._poll)
EMIC:endif
```

**src/led.c:**
```c
void LEDs_.{name}._init(void) {
    HAL_GPIO_PinCfg(.{pin}., GPIO_OUTPUT);
}

EMIC:ifdef usedFunction.LEDs_.{name}._state
void LEDs_.{name}._state(uint8_t status) {
    switch (status) {
        case 0: HAL_GPIO_PinSet(.{pin}., GPIO_LOW);  break;
        case 1: HAL_GPIO_PinSet(.{pin}., GPIO_HIGH); break;
        case 2: /* toggle */ break;
    }
}
EMIC:endif

EMIC:ifdef usedFunction.LEDs_.{name}._blink
static uint16_t blink_timeOn_.{name}.;
static uint16_t blink_period_.{name}.;
static uint16_t blink_count_.{name}.;
static uint32_t blink_timer_.{name}.;

void LEDs_.{name}._blink(uint16_t timeOn, uint16_t period, uint16_t times) {
    blink_timeOn_.{name}. = timeOn;
    blink_period_.{name}. = period;
    blink_count_.{name}. = times;
    blink_timer_.{name}. = systemTimer_get();
}

void LEDs_.{name}._poll(void) {
    // State machine para blink no-bloqueante
    // Usa systemTimer para medir tiempos
}
EMIC:endif
```

**Puntos clave:**
- Parametros `name=` y `pin=` permiten multiples instancias (led1, led2, ...)
- Poll solo se registra si el integrador usa `blink` (`usedFunction`)
- Init siempre se registra (configura el GPIO)
- GPIO es periferico interno → acceso directo a HAL (no necesita driver)
- SystemTimer es un driver utilitario incluido directamente

### 8.2. API con Eventos — Timer

Temporizadores con multiples instancias y eventos. Demuestra: instanciacion
multiple, compilacion condicional de poll por evento, modos de operacion.

**timer_api.emic:**
```
EMIC:tag(driverName = TIME)

/**
* @fn void setTime.{name}.(uint16_t time, char mode);
* @alias setTime.{name}.
* @brief Starts the timer
* @param time Time in milliseconds.
* @param mode T:timer, A:auto-reload.
* @return Nothing
*/

/**
* @fn extern void etOut.{name}.(void);
* @alias timeOut.{name}.
* @brief When the time configured in the timer was reached.
*/

EMIC:setInput(DEV:_drivers/SystemTimer/systemTimer.emic)

EMIC:copy(inc/timer_api.h > TARGET:inc/timer_api.{name}..h, name=.{name}.)
EMIC:copy(src/timer_api.c > TARGET:timer_api.{name}..c, name=.{name}.)

EMIC:define(main_includes.timer_api.{name}., timer_api.{name}.)
EMIC:define(c_modules.timer_api.{name}., timer_api.{name}.)
```

**inc/timer_api.h:**
```c
EMIC:ifdef usedFunction.setTime.{name}.
void setTime.{name}.(uint32_t setPoint, char l_modo);

void timer.{name}._init(void);
EMIC:define(inits.timer.{name}., timer.{name}._init)

EMIC:ifdef usedEvent.etOut.{name}.
void timer.{name}._Poll(void);
EMIC:define(polls.timer.{name}., timer.{name}._Poll)
EMIC:endif

EMIC:endif
```

**src/timer_api.c:**
```c
static uint32_t timer_setpoint_.{name}.;
static uint32_t timer_start_.{name}.;
static char timer_mode_.{name}.;
static uint8_t timer_active_.{name}.;

void timer.{name}._init(void) {
    timer_active_.{name}. = 0;
}

void setTime.{name}.(uint32_t setPoint, char l_modo) {
    timer_setpoint_.{name}. = setPoint;
    timer_mode_.{name}. = l_modo;
    timer_start_.{name}. = systemTimer_get();
    timer_active_.{name}. = 1;
}

EMIC:ifdef usedEvent.etOut.{name}.
void timer.{name}._Poll(void) {
    if (timer_active_.{name}.) {
        if ((systemTimer_get() - timer_start_.{name}.) >= timer_setpoint_.{name}.) {
            if (timer_mode_.{name}. == 'A') {
                timer_start_.{name}. = systemTimer_get();  // auto-reload
            } else {
                timer_active_.{name}. = 0;                 // one-shot
            }
            etOut.{name}.();
        }
    }
}
EMIC:endif
```

**Puntos clave:**
- `name=1`, `name=2` → crea `setTime1()`, `setTime2()`, etc.
- Poll solo se compila si el integrador usa el evento `etOut{name}`
- Init solo se compila si el integrador usa la funcion `setTime{name}`
- Modo `'T'` = one-shot timer, modo `'A'` = auto-reload

### 8.3. API de Comunicacion — RS232

Comunicacion serial con seleccion de protocolo. Demuestra: JSON configurator,
compilacion condicional por config, streams, funciones variadic, driver
inyectado desde el modulo.

**rs232.emic:**
```
EMIC:tag(driverName = RS232)

EMIC:json(type = configurator)
{
    "brief": "El protocolo define el formato de los datos enviados y recibidos",
    "legend": "seleccione protocolo",
    "name": "RS232prot",
    "options": [
        { "legend": "EMIC Message", "value": "EMIC_message" },
        { "legend": "TEXT Message", "value": "TEXT_line" }
    ]
}

EMIC:if(.{config.RS232prot}.==EMIC_message)
/**
* @fn void pRS232(char* format,...);
* @alias Send_EMIC(concat tag, concat msg)
* @brief send data as EMIC message format
*/
EMIC:endif

EMIC:if(.{config.RS232prot}.==TEXT_line)
/**
* @fn variadic pRS232(char* format,...);
* @alias Send_TEXT(concat msg)
* @brief send data as text line
*/
EMIC:endif

/**
* @fn extern void eRS232(void);
* @alias RS232_received
* @brief Triggered when a complete message is received
*/

// El driver UART es inyectado por el modulo via parametro "driver"
// La API solo necesita incluir el header del driver inyectado
EMIC:setInput(DEV:_system/Stream/stream.emic)
EMIC:setInput(DEV:_system/Stream/streamOut.emic)
EMIC:setInput(DEV:_system/Stream/streamIn.emic)

EMIC:copy(inc/rs232.h > TARGET:inc/rs232.h, driver=.{driver}.)
EMIC:copy(src/rs232.c > TARGET:rs232.c, driver=.{driver}., BufferSize=.{BufferSize}.)

EMIC:define(main_includes.RS232, RS232)
EMIC:define(c_modules.RS232, RS232)
```

**inc/rs232.h:**
```c
#include "inc/.{driver}..h"    // header del driver inyectado

void RS232_Init(void);
EMIC:define(inits.RS232_Init, RS232_Init)

void pRS232(char* format, ...);

void Poll_RS232(void);
EMIC:define(polls.Poll_RS232, Poll_RS232)
```

**src/rs232.c (fragmento):**
```c
static uint8_t rxBuffer[.{BufferSize}.];
static uint16_t rxHead, rxTail;

// Callback registrado en el driver inyectado.
// La ISR de _hard/ invoca al driver, el driver invoca este callback.
void RS232_rxCallback(uint8_t d) {
    rxBuffer[rxHead] = d;
    rxHead = (rxHead + 1) % .{BufferSize}.;
}

void RS232_Init(void) {
    .{driver}._init();     // → init del driver inyectado
    rxHead = 0;
    rxTail = 0;
}

void Poll_RS232(void) {
    while (rxTail != rxHead) {
        uint8_t byte = rxBuffer[rxTail];
        rxTail = (rxTail + 1) % .{BufferSize}.;
        // Parseo de protocolo (EMIC message o TEXT line)
        // Cuando se completa un mensaje:
        EMIC:ifdef usedEvent.eRS232
        eRS232();
        EMIC:endif
    }
}
```

**Puntos clave:**
- JSON configurator genera wizard en el editor (EMIC_message vs TEXT_line)
- `BufferSize` se consume aqui en la API (buffer circular)
- Driver inyectado por el modulo via `driver=uart1`
- La API no sabe que driver hay debajo — solo usa `.{driver}._init()`, `.{driver}._sendByte()`, etc.
- Streams de `_system/` para funciones de envio formateadas
- Funcion variadic `pRS232(char* format, ...)` con format specifiers EMIC (`$s`, `$r`)

**Invocacion desde generate.emic:**
```
// El modulo instancia el driver primero, luego la API
EMIC:setInput(DEV:_drivers/USB/MCP2200/MCP2200.emic, port=1, baud=9600, name=uart1)
EMIC:setInput(DEV:_api/Wired_Communication/RS232/rs232.emic, driver=uart1, BufferSize=512)
```

### 8.4. API de Sensor Complejo — LoadCell

Celda de carga con filtrado digital, deteccion de estabilidad, tara y
calibracion. Demuestra: driver inyectado con callback inverso, multiples
eventos, variables exportadas, JSON configurator, maquina de estados.

**LoadCell.emic:**
```
EMIC:tag(driverName = LoadCell)

EMIC:json(type = configurator)
{
    "name": "FilterLength",
    "brief": "Configuracion del filtro de la celda de carga",
    "legend": "Seleccione la longitud del filtro",
    "options": [
        { "legend": "Corto (8 muestras)", "value": "8",
          "brief": "Respuesta rapida, menor filtrado" },
        { "legend": "Medio (32 muestras)", "value": "32",
          "brief": "Balance entre velocidad y filtrado" },
        { "legend": "Largo (64 muestras)", "value": "64",
          "brief": "Respuesta lenta, mejor filtrado" }
    ]
}

/**
* @fn void setZero(void);
* @alias Zero
* @brief Sets current value as zero.
* @return Nothing
*/

/**
* @fn void setCalibration(float Calibration_Weight);
* @alias Calibrate
* @brief Sets a calibration weight
* @param Calibration_Weight Known weight for calibration
* @return Nothing
*/

/**
* @var float Capacidad;
* @alias Capacidad
* @brief Maximum weight for which the load cell is linear
*/

/**
* @var float mVxV;
* @alias mVxV
* @brief Sensitivity of the load cell in mV/V
*/

/**
* @fn extern void eStable(void);
* @alias Stable
* @brief When the weight value is stable.
*/

/**
* @fn extern void eZero(void);
* @alias Zero
* @brief When the weight is stable at zero.
*/

/**
* @fn extern void eUnstable(void);
* @alias Unstable
* @brief When the weight is changing.
*/

/**
* @fn extern void eOverLoad(void);
* @alias OverLoad
* @brief When the weight exceeds capacity.
*/

// El driver ADC es inyectado por el modulo via parametro "driver"
EMIC:copy(inc/LoadCell.h > TARGET:inc/LoadCell.h, driver=.{driver}., FilterLength=.{config.FilterLength}.)
EMIC:copy(src/LoadCell.c > TARGET:LoadCell.c, driver=.{driver}., FilterLength=.{config.FilterLength}.)

EMIC:define(main_includes.LoadCell, LoadCell)
EMIC:define(c_modules.LoadCell, LoadCell)
```

**inc/LoadCell.h:**
```c
#define HistoryLength .{FilterLength}.

extern float Capacidad;
extern float mVxV;
extern int32_t filterOut;

void init_LoadCell(void);
EMIC:define(inits.LoadCell, init_LoadCell)

void poll_LoadCell(void);
EMIC:define(polls.LoadCell, poll_LoadCell)

void setZero(void);

EMIC:ifdef usedFunction.setCalibration
void setCalibration(float Calibration_Weight);
EMIC:endif

EMIC:ifdef usedEvent.eStable
extern void eStable(void);
EMIC:endif

EMIC:ifdef usedEvent.eZero
extern void eZero(void);
EMIC:endif

EMIC:ifdef usedEvent.eUnstable
extern void eUnstable(void);
EMIC:endif

EMIC:ifdef usedEvent.eOverLoad
extern void eOverLoad(void);
EMIC:endif
```

**src/LoadCell.c (fragmento):**
```c
float Capacidad;
float mVxV;
int32_t filterOut;

static int32_t Historial[HistoryLength];
static int32_t Acumulador;
static uint8_t Indice;
static uint16_t Balanza_flags;

#define F_Stable             0x0001
#define F_StableEventTrigger 0x0002
#define F_UnstableEventTrigger 0x0004

void init_LoadCell(void) {
    .{driver}._init();    // → init del driver ADC inyectado
    Balanza_flags = 0;
    Acumulador = 0;
    Indice = 0;
    for (int i = 0; i < HistoryLength; i++)
        Historial[i] = 0;
}

// Callback inverso — el driver ADS1231 llama a esta funcion
// desde su ISR cuando hay una nueva lectura del ADC
void nuevaLectura(int32_t adcValue) {
    Acumulador -= Historial[Indice];
    Acumulador += adcValue;
    Historial[Indice] = adcValue;
    Indice = (Indice + 1) % HistoryLength;
    filterOut = Acumulador / HistoryLength;
}

void poll_LoadCell(void) {
    if ((Balanza_flags & F_Stable)) {
        if (!(Balanza_flags & F_StableEventTrigger)) {
            Balanza_flags |= F_StableEventTrigger;
            Balanza_flags &= ~F_UnstableEventTrigger;
            if (Peso_neto_f == 0) {
                EMIC:ifdef usedEvent.eZero
                eZero();
                EMIC:endif
            } else {
                EMIC:ifdef usedEvent.eStable
                eStable();
                EMIC:endif
            }
        }
    } else {
        if (!(Balanza_flags & F_UnstableEventTrigger)) {
            Balanza_flags |= F_UnstableEventTrigger;
            Balanza_flags &= ~F_StableEventTrigger;
            EMIC:ifdef usedEvent.eUnstable
            eUnstable();
            EMIC:endif
        }
    }
}
```

**Puntos clave:**
- Driver ADC inyectado por el modulo via `driver=adc`
- Callback inverso: el driver declara `extern void nuevaLectura()`, la API la implementa
- Init encadenado: `init_LoadCell()` → `.{driver}._init()` (driver inyectado)
- Filtro promedio movil con longitud configurable via JSON configurator
- Multiples eventos condicionales (eStable, eZero, eUnstable, eOverLoad)
- Variables exportadas (`Capacidad`, `mVxV`) visibles en Discovery
- Maquina de estados con flags para deteccion de estabilidad

**Invocacion desde generate.emic:**
```
// El modulo instancia el driver ADC primero, luego la API
EMIC:setInput(DEV:_drivers/ADC/ADS1231/ADS1231.emic, name=adc)
EMIC:setInput(DEV:_api/Sensors/LoadCell/LoadCell.emic, driver=adc)
```

---

## 9. Cadena de Dependencias — Ejemplo Completo

### LoadCell (sensor de peso — driver inyectado)

```
Modulo HRD_LOAD_CELL (generate.emic)
│
├── EMIC:setInput(DEV:_pcb/pcb.emic, pcb=HRD_LOAD_CELL)
│   └── Define system.ucName = dsPIC33EP512MC806
│
├── EMIC:setInput(DEV:_drivers/ADC/ADS1231/ADS1231.emic, name=adc)
│   │   ← El modulo instancia el driver PRIMERO
│   │
│   ├── ADS1231.emic incluye:
│   │   └── EMIC:setInput(DEV:_hal/GPIO/gpio.emic)
│   │       └── gpio.emic → DEV:_hard/.../GPIO/gpio.emic
│   │
│   ├── Copia a TARGET:inc/adc.h y TARGET:adc.c
│   ├── adc.h: extern void nuevaLectura(int32_t)  ← callback inverso
│   └── adc.c: ISR llama nuevaLectura()
│
├── EMIC:setInput(DEV:_api/Sensors/LoadCell/LoadCell.emic, driver=adc)
│   │   ← La API recibe referencia al driver inyectado
│   │
│   ├── LoadCell.emic: incluye _system/ si lo necesita
│   ├── LoadCell.h: #include "inc/adc.h", init, poll, eventos, variables
│   ├── LoadCell.c: implementa nuevaLectura(), adc_init(), filtro, estabilidad
│   └── Registra: inits.LoadCell, polls.LoadCell
│
├── EMIC:setInput(DEV:_api/Wired_Communication/EMICBus/EMICBus.emic, driver=i2c1)
│   └── ... (I2C stack — driver inyectado previamente)
│
└── EMIC:setInput(DEV:_api/Indicators/LEDs/led.emic, name=led, pin=Led1)
    └── ... (GPIO via HAL directo — periferico interno)
```

### USB Module (comunicacion serial — driver inyectado)

```
Modulo USB (generate.emic)
│
├── EMIC:setInput(DEV:_drivers/USB/MCP2200/MCP2200.emic,
│                 port=1, baud=9600, name=uart1)
│   │   ← El modulo instancia el driver con parametros especificos
│   │
│   ├── MCP2200.emic incluye:
│   │   └── DEV:_hal/UART/UART.emic (port=1, baud=9600,
│   │       rxCallback=..., txCallback=...)
│   │       └── DEV:_hard/.../UART/UART.emic
│   │           (ISR invoca rxCallback/txCallback)
│   │
│   └── Copia a TARGET:inc/uart1.h y TARGET:uart1.c
│
├── EMIC:setInput(DEV:_api/Wired_Communication/RS232/rs232.emic,
│                 driver=uart1, BufferSize=512)
│   │   ← La API solo recibe nombre del driver + sus propios parametros
│   │
│   ├── rs232.emic incluye: _system/Stream/* (streams)
│   ├── rs232.h: #include "inc/uart1.h", init/poll
│   ├── rs232.c: buffer circular (512), usa uart1_init(), uart1_sendByte()
│   └── Registra: inits.RS232_Init, polls.Poll_RS232
│
└── ... (LEDs, timers, etc.)
```

---

## 10. Reglas para Crear Nuevas APIs

Estas 10 reglas deben seguirse al crear cualquier API nueva:

| # | Regla | Justificacion |
|---|-------|---------------|
| 1 | **Independencia del modulo**: La API no contiene logica especifica de ningun modulo. Si algo es especifico del proyecto, va en `program.xml`. | Reutilizacion en multiples modulos |
| 2 | **Inyeccion de drivers**: El modulo instancia el driver/HAL y pasa `driver=nombre` a la API. La API no construye rutas de driver. | Desacoplamiento, flexibilidad |
| 3 | **Eventos opcionales**: Todo evento protegido con `EMIC:ifdef usedEvent.nombre`. Nunca asumir que el integrador implementa un evento. | Zero-cost abstraction |
| 4 | **Ejecucion no-bloqueante**: El `poll` no tarda mas de microsegundos. Usar maquinas de estados y flags para logica temporal. | Modelo cooperativo |
| 5 | **Registrar init/poll**: Siempre usar `EMIC:define(inits.X, X)` y `EMIC:define(polls.X, X)`. Drivers/HAL NO registran los suyos. | Cadena de control unica |
| 6 | **Discovery completo**: Toda funcion, evento y variable publica tiene comentarios Doxygen con `@fn`/`@var`, `@alias`, `@brief`, `@param`. | Visibilidad para el integrador |
| 7 | **Registro de modulo**: Terminar el `.emic` con `EMIC:define(main_includes.X, X)` y `EMIC:define(c_modules.X, X)`. | Inclusion automatica en build |
| 8 | **Contrato de nombres**: Si la API consume un driver inyectado, usar `.{driver}._funcion()` para invocar funciones del contrato. El driver y la API deben acordar los nombres. | Interoperabilidad |
| 9 | **Retrocompatibilidad**: Nunca modificar la firma de funciones existentes. Agregar nuevas funciones condicionadas con `EMIC:ifdef`. | Estabilidad del SDK |
| 10 | **Configuradores**: Si la API tiene opciones de usuario, declararlas con `EMIC:json(type=configurator)` para que aparezcan en el wizard del editor. | Experiencia del integrador |

---

## 11. Impacto en Capas Adyacentes

### Hacia arriba: Modulo → API

El modulo consume la API via `EMIC:setInput` en su `generate.emic`:

```
EMIC:setInput(DEV:_api/Sensors/LoadCell/LoadCell.emic)
EMIC:setInput(DEV:_api/Indicators/LEDs/led.emic, name=led1, pin=Led1)
EMIC:setInput(DEV:_api/Timers/timer_api.emic, name=1)
```

El modulo:
- Selecciona que APIs usar
- Pasa parametros de configuracion (driver, port, name, pin, ...)
- Implementa eventos en `program.xml`
- Consume funciones y variables publicadas por Discovery

### Hacia abajo: API → HAL / Drivers / Middleware / System

```
_api/
 │
 ├── consume _hal/ → perifericos internos del MCU (acceso directo)
 │   Patron: EMIC:setInput(DEV:_hal/GPIO/gpio.emic)
 │
 ├── consume _drivers/ → hardware externo (inyectado por el modulo)
 │   Patron: driver=nombre → #include "inc/.{driver}..h"
 │
 ├── consume _middleware/ → bloques de procesamiento
 │   Patron: EMIC:setInput(DEV:_middleware/{tipo}/{bloque}.emic, ...)
 │
 └── consume _system/ → utilidades compartidas
     Patron: EMIC:setInput(DEV:_system/Stream/stream.emic)
```

### Relacion con `_hal/`

La API puede acceder a HAL directamente para perifericos internos del MCU.
No se necesita un "driver" intermedio — HAL ya provee la abstraccion:

```
// Perifericos internos → HAL directo
EMIC:setInput(DEV:_hal/GPIO/gpio.emic)     // GPIO
EMIC:setInput(DEV:_hal/Timer/timer.emic)    // Timer interno
EMIC:setInput(DEV:_hal/ADC/adc.emic)        // ADC interno
EMIC:setInput(DEV:_hal/RTC/RTC.emic)        // RTC interno
```

`_drivers/` queda **exclusivamente** para hardware externo al MCU
(chips con datasheet propio, conectados via SPI/I2C/GPIO/UART).

### Relacion con `_system/`

La capa `_system/` contiene:
- **Streams** (`streamIn_t`, `streamOut_t`): Abstraccion de I/O formateado
- **Conversiones**: Funciones de conversion de unidades
- **Includes comunes**: Headers compartidos entre APIs

Las APIs de comunicacion (RS232, USB, EMICBus) son las principales consumidoras
de streams. Las APIs de sensores usan las funciones de conversion.

**Regla**: `_api/` SI puede acceder a `_system/`. `_hard/` NO puede (ver M4).

---

## 12. Validacion y Verificacion

### Checklist para una nueva API

| # | Verificacion | Como validar |
|---|-------------|-------------|
| 1 | Archivo `.emic` tiene `EMIC:tag(driverName = ...)` | Grep por `EMIC:tag` |
| 2 | Todas las funciones publicas tienen `@fn`, `@alias`, `@brief` | Discovery las encuentra |
| 3 | Todos los eventos tienen `EMIC:ifdef usedEvent.X` en `.h` y `.c` | Compilar sin implementar eventos → no debe haber error |
| 4 | Init/poll registrados con `EMIC:define(inits.X / polls.X)` | Aparecen en main.c generado |
| 5 | main_includes y c_modules registrados | Archivo se incluye y compila |
| 6 | Init encadenado llama a init de capas inferiores | Perifericos se inicializan correctamente |
| 7 | Poll no es bloqueante | No hay `delay()`, `while(flag)` ni loops largos |
| 8 | Parametros del modulo se propagan correctamente | Cambiar param en generate.emic → refleja en .c generado |
| 9 | Instanciacion multiple funciona (si aplica) | Crear 2 instancias → sin conflictos de nombres |
| 10 | Sin acceso directo a `_hard/` | Grep por `_hard/` en el .emic → debe ser cero (HAL si esta permitido) |
| 11 | Variables exportadas tienen `extern` en .h y definicion en .c | Linkea sin errores |
| 12 | JSON configurator tiene formato valido (si aplica) | Wizard aparece en el editor |

### Proceso de verificacion

1. **Compilacion**: El modulo compila sin errores con la nueva API
2. **Discovery**: Las funciones, eventos y variables aparecen en el editor
3. **program.xml**: El integrador puede arrastrar los recursos al programa visual
4. **Generacion**: `generate.emic` produce codigo C correcto en `TARGET:/`
5. **Ejecucion**: El firmware funciona correctamente en hardware real

---

## 13. Glosario

| Termino | Definicion |
|---------|-----------|
| **API EMIC** | Capa de abstraccion funcional entre modulo y driver |
| **Driver** | Codigo de control para hardware **externo** al MCU (chips con datasheet propio) |
| **HAL** | Hardware Abstraction Layer para perifericos internos del MCU |
| **Hard** | Codigo especifico del microcontrolador (registros, ISR) |
| **Middleware** | Bloques de procesamiento intermedios (filtros, detectores, colas) |
| **Modulo** | Unidad funcional completa (PCB + firmware + configuracion) |
| **Discovery** | Proceso que indexa recursos publicados (`@fn`, `@var`, eventos) |
| **Configurator** | Wizard generado automaticamente a partir de `EMIC:json` |
| **program.xml** | Script visual del integrador con logica de negocio |
| **generate.emic** | Script de metaprogramacion que ensambla el proyecto |
| **EMIC:setInput** | Directiva que incluye y procesa recursivamente un archivo |
| **EMIC:copy** | Directiva que copia un archivo con sustitucion de parametros. Equivalente a `setOutput/setInput/restoreOutput` para un solo archivo. |
| **EMIC:define** | Directiva que define una macro (clave=valor) |
| **EMIC:ifdef** | Compilacion condicional basada en existencia de macro |
| **EMIC:tag** | Identifica la API para el sistema de Discovery |
| **init** | Funcion de inicializacion ejecutada una vez al arrancar |
| **poll** | Funcion de sondeo ejecutada en cada ciclo del main loop |
| **usedEvent** | Macro definida automaticamente cuando el integrador usa un evento |
| **usedFunction** | Macro definida automaticamente cuando el integrador usa una funcion |
| **rxCallback** | Funcion callback invocada por la ISR de `_hard/` al recibir un byte |
| **txCallback** | Funcion callback invocada por la ISR de `_hard/` para obtener un byte a transmitir |
| **Inyeccion de driver** | Patron donde el modulo instancia el driver/HAL primero y pasa una referencia (`driver=nombre`) a la API |
| **Contrato de nombres** | Convencion implicita: el driver inyectado como `name=X` expone funciones `X_init()`, `X_read()`, etc. que la API espera |
| **Virtual path** | Ruta logica (`DEV:`, `TARGET:`, `SYS:`, `USER:`) |
| **Zero-cost abstraction** | Codigo que desaparece del binario si no se usa (via `EMIC:ifdef`) |
