# EMIC API — Definicion, Arquitectura y Rol en el Framework

> Documento de referencia para el DevAgent. Basado en el analisis exhaustivo del SDK
> `EMIC_IA_M` (APIs, drivers, HAL, hard, modules).

---

## 1. Que es una API EMIC

Una **API EMIC** es una **capa de abstraccion de alto nivel** que expone funciones,
variables y eventos para la logica de negocio de una aplicacion embebida. Se ubica
entre el **modulo** (capa de aplicacion) y el **driver** (capa de hardware externo),
cumpliendo tres roles fundamentales:

1. **Abstraccion de hardware** — Oculta las diferencias entre drivers que cumplen la
   misma funcion (ej: distintos chips de ADC, distintos transceivers USB). El modulo
   consume la API sin saber que driver hay debajo.

2. **Procesamiento intermedio** — Implementa logica de procesamiento que no es
   responsabilidad del driver ni del modulo: filtros digitales, maquinas de estados,
   conversiones de unidades, buffers circulares, protocolos de alto nivel.

3. **Publicacion de recursos** — Declara funciones, variables y eventos que el sistema
   Discovery indexa y que el integrador consume desde `program.xml` en el editor EMIC.

### Principio fundamental

> La API es **independiente del modulo que la consume**. Multiples modulos pueden usar
> la misma API con distintos drivers y configuraciones. La API nunca contiene logica de
> negocio especifica de un proyecto.

---

## 2. Ubicacion en la Arquitectura de Capas

```
┌─────────────────────────────────────────────────────────┐
│  MODULO  (generate.emic + program.xml)                  │
│  Logica de negocio, configuracion, proyecto del usuario │
├─────────────────────────────────────────────────────────┤
│  API  (_api/)                                           │
│  Abstraccion funcional: funciones, variables, eventos   │
│  Registra inits y polls. Consume drivers.               │
├─────────────────────────────────────────────────────────┤
│  DRIVER  (_drivers/)                                    │
│  Control de hardware externo (chips, sensores, etc.)    │
│  Consume HAL para acceder a perifericos del MCU.        │
├─────────────────────────────────────────────────────────┤
│  HAL  (_hal/)                                           │
│  Abstraccion de perifericos internos del MCU            │
│  (UART, SPI, I2C, GPIO, Timer, ADC interno, etc.)      │
├─────────────────────────────────────────────────────────┤
│  HARD  (_hard/{mcuName}/)                               │
│  Codigo especifico del microcontrolador                 │
│  Registros, interrupciones, configuracion de pines      │
└─────────────────────────────────────────────────────────┘
```

**Regla de dependencia**: Cada capa solo conoce a la capa inmediatamente inferior.
La API no accede directamente a `_hard`; delega al driver, que delega al HAL.

---

## 3. Estructura de Archivos de una API

Cada API reside en `_api/{Categoria}/{NombreAPI}/` y contiene tipicamente:

```
_api/
└── {Categoria}/
    └── {NombreAPI}/
        ├── {NombreAPI}.emic      # Orquestador: dependencias + copy/setInput
        ├── inc/
        │   └── {NombreAPI}.h     # Declaraciones, Discovery metadata, init/poll registration
        └── src/
            └── {NombreAPI}.c     # Implementacion: init, poll, funciones, eventos
```

### 3.1. Archivo `.emic` (Orquestador)

El archivo `.emic` es un script de metaprogramacion que:
- Incluye dependencias (drivers, HAL, system libraries)
- Copia sus propios `.h` y `.c` al `TARGET:`
- Recibe parametros del modulo y los propaga hacia abajo

**Ejemplo — USB_API.emic:**
```
EMIC:setInput(DEV:_system/Stream/stream.emic)
EMIC:setInput(DEV:_system/Stream/streamOut.emic)
EMIC:setInput(DEV:_system/Stream/streamIn.emic)
EMIC:setInput(DEV:_drivers/USB/.{driver}./.{driver}..emic,port=.{port}.,BufferSize=.{BufferSize}.,baud=.{baud}.,driver=.{driver}.)

EMIC:setOutput(TARGET:inc/USB_API.h)
EMIC:setInput(inc/USB_API.h)
EMIC:restoreOutput

EMIC:copy(src/USB_API.c > TARGET:USB_API.c)

EMIC:define(main_includes.USB_API,USB_API)
EMIC:define(c_modules.USB_API,USB_API)
```

### 3.2. Archivo `.h` (Interfaz + Discovery + Registros)

El header cumple multiples funciones:

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
EMIC:define(inits.init_ADC,init_ADC)

void poll_ADC(void);
EMIC:define(polls.poll_ADC,poll_ADC)
```

### 3.3. Archivo `.c` (Implementacion)

El source implementa:
- `init_*()` — Inicializacion de la API + inicializacion encadenada de drivers/HAL
- `poll_*()` — Logica no-bloqueante ejecutada en cada ciclo del main loop
- Funciones publicadas (las que Discovery indexa)
- Invocacion condicional de eventos

**Ejemplo — poll_LoadCell():**
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

---

## 4. Patrones Arquitectonicos Clave

### 4.1. Cascada de Parametros

Los parametros fluyen desde el modulo hacia abajo a traves de placeholders `.{param}.`:

```
Modulo (generate.emic):
  EMIC:setInput(DEV:_api/Wired_Communication/USB/USB_API.emic,
                driver=MCP2200, port=1, BufferSize=512, baud=9600)
      ↓
API (USB_API.emic):
  EMIC:setInput(DEV:_drivers/USB/.{driver}./.{driver}..emic,
                port=.{port}., BufferSize=.{BufferSize}., baud=.{baud}.)
      ↓
Driver (MCP2200.emic):
  EMIC:setInput(DEV:_hal/UART/UART.emic,
                port=.{port}., BufferSize=.{BufferSize}., baud=.{baud}.)
      ↓
HAL (UART.emic):
  EMIC:setInput(DEV:_hard/.{system.ucName}./UART/UARTX.emic,
                port=.{port}., BufferSize=.{BufferSize}., baud=.{baud}.)
```

Cada capa recibe los parametros, usa los que necesita y propaga el resto.

### 4.2. Eventos Opt-in (Zero-Cost Abstraction)

Los eventos se declaran en la API pero solo se compilan si el integrador los usa
en `program.xml`. El mecanismo es `EMIC:ifdef usedEvent.{nombre}`:

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

### 4.3. Registro de Init/Poll

Las APIs registran sus funciones `init` y `poll` usando macros que el sistema
`main.emic` recolecta:

```c
// En el .h de la API
EMIC:define(inits.init_LoadCell, init_LoadCell)
EMIC:define(polls.poll_LoadCell, poll_LoadCell)
```

**Regla critica:** Solo la API registra inits/polls. Los drivers y HAL NO registran
los suyos — son llamados en cadena desde la API:

```c
void init_LoadCell(void) {
    ADS1231_init();        // → driver init
    // ... configuracion propia de la API ...
}

void poll_LoadCell(void) {
    // El driver ADS1231 usa interrupciones + callback, no necesita poll
    // ... logica de filtrado y deteccion de estabilidad ...
}
```

### 4.4. Intercambiabilidad de Drivers

Las APIs aceptan un parametro `driver=` que selecciona la implementacion concreta:

```
EMIC:setInput(DEV:_api/Wired_Communication/USB/USB_API.emic, driver=MCP2200, ...)
EMIC:setInput(DEV:_api/Wired_Communication/USB/USB_API.emic, driver=FT232,  ...)
```

La API incluye al driver dinamicamente:
```
EMIC:setInput(DEV:_drivers/USB/.{driver}./.{driver}..emic, ...)
```

Todos los drivers de una misma categoria exponen funciones con nombres identicos
(ej: todos los sensores de temperatura implementan `getTemperature()`), permitiendo
intercambiarlos sin modificar la API.

### 4.5. Callback Inverso (Driver → API)

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
circular.

### 4.6. Instanciacion Multiple (Parametros de Nombre)

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
y registra sus propios inits/polls independientes.

### 4.7. Configurador UI (EMIC:json)

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
compilacion.

---

## 5. Discovery: Publicacion de Recursos

El proceso Discovery escanea los archivos `.h` de las APIs buscando comentarios
Doxygen con tags especiales:

| Tag | Tipo | Ejemplo |
|-----|------|---------|
| `@fn void func(...)` | Funcion | `@fn void start_ADC(uint8_t Freq, uint32_t Qty)` |
| `@fn extern void event(...)` | Evento | `@fn extern void eADC(int32_t Result)` |
| `@var tipo nombre` | Variable | `@var float Capacidad` |
| `@alias` | Nombre amigable | `@alias DataReady` |
| `@brief` | Descripcion | `@brief Dato del ADC listo` |
| `@param` | Parametro | `@param Freq Frecuencia de muestreo` |

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

---

## 6. Ejecucion No-Bloqueante

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

## 7. Modulo de Registro y Compilacion

Cada API se registra con dos macros al final de su `.emic`:

```c
EMIC:define(main_includes.NombreAPI, NombreAPI)   // → #include "inc/NombreAPI.h"
EMIC:define(c_modules.NombreAPI, NombreAPI)        // → NombreAPI.c en el proyecto
```

El sistema de build recolecta todos los `main_includes.*` para generar los includes
en `main.c`, y todos los `c_modules.*` para generar la lista de archivos `.c` del
proyecto MPLAB-X.

---

## 8. Cadena de Dependencias — Ejemplo Completo

### LoadCell API (sensor de peso)

```
Modulo HRD_LOAD_CELL (generate.emic)
│
├── EMIC:setInput(DEV:_pcb/pcb.emic, pcb=HRD_LOAD_CELL)
│   └── Define system.ucName = dsPIC33EP512MC806
│
├── EMIC:setInput(DEV:_api/Sensors/LoadCell/LoadCell.emic)
│   │
│   ├── LoadCell.emic incluye:
│   │   └── EMIC:setInput(DEV:_drivers/ADC/ADS1231/ADS1231.emic)
│   │       │
│   │       ├── ADS1231.emic incluye:
│   │       │   └── EMIC:setInput(DEV:_hal/GPIO/gpio.emic)
│   │       │       └── gpio.emic incluye:
│   │       │           └── EMIC:setInput(DEV:_hard/dsPIC33EP512MC806/GPIO/gpio.emic)
│   │       │
│   │       ├── ADS1231.h: declare extern void nuevaLectura(int32_t)
│   │       └── ADS1231.c: interrupt handler llama nuevaLectura()
│   │
│   ├── LoadCell.h: init, poll, funciones, eventos, variables
│   ├── LoadCell.c: implementa nuevaLectura(), filtro, estabilidad
│   └── Registra: inits.init_LoadCell, polls.poll_LoadCell
│
├── EMIC:setInput(DEV:_api/Wired_Communication/EMICBus/EMICBus.emic)
│   └── ... (I2C stack completo)
│
├── EMIC:setInput(DEV:_api/Indicators/LEDs/led.emic, name=led, pin=Led1)
│   └── ... (GPIO para LED indicador)
│
└── EMIC:copy(... program.xml expansion ...)
    └── Logica del integrador usando recursos de LoadCell + EMICBus + LED
```

### USB Module (comunicacion serial)

```
Modulo USB (generate.emic)
│
├── EMIC:setInput(DEV:_api/Wired_Communication/USB/USB_API.emic,
│                 driver=MCP2200, port=1, BufferSize=512, baud=9600)
│   │
│   ├── USB_API.emic incluye:
│   │   ├── DEV:_system/Stream/stream.emic (sistema de streams)
│   │   ├── DEV:_system/Stream/streamOut.emic
│   │   ├── DEV:_system/Stream/streamIn.emic
│   │   └── DEV:_drivers/USB/MCP2200/MCP2200.emic
│   │       └── MCP2200.emic incluye:
│   │           └── DEV:_hal/UART/UART.emic (port=1, baud=9600)
│   │               └── DEV:_hard/dsPIC33EP512MC806/UART/UARTX.emic
│   │
│   ├── USB_API.h: funciones de envio/recepcion, eventos
│   ├── USB_API.c: streams, parseo de mensajes, protocolos
│   └── Registra: inits.init_USB, polls.poll_USB
│
└── ... (LEDs, timers, etc.)
```

---

## 9. Catalogo de APIs Existentes en el SDK

| Categoria | API | Funcion |
|-----------|-----|---------|
| **ADC** | ADC | Conversion analogico-digital con LTC2500 + PGA280 |
| **Sensors** | LoadCell | Celda de carga con filtrado, estabilidad, tara, calibracion |
| **Sensors** | ForceSensor | Sensor de fuerza con filtrado y eventos |
| **Sensors** | AnalogInput | Entrada analogica generica (ADC interno) |
| **Timers** | timer_api | Temporizadores con eventos (multi-instancia) |
| **Indicators** | LEDs (led) | Control de LEDs con patrones de parpadeo |
| **Indicators** | DigitalInputs | Entradas digitales con anti-rebote y eventos |
| **Communication** | USB_API | Comunicacion USB via driver serial (MCP2200, FT232) |
| **Communication** | EMICBus | Protocolo I2C para sistema modular EMIC |
| **Protocols** | Modbus | Protocolo Modbus RTU/TCP |
| **Protocols** | DinaModbus | Modbus especializado para sistemas dinamometricos |

---

## 10. Reglas para la Creacion de Nuevas APIs (DevAgent)

1. **Independencia del modulo**: La API no debe contener logica especifica de ningun
   modulo. Si algo es especifico del proyecto, va en `program.xml`.

2. **Intercambiabilidad de drivers**: Si la API puede funcionar con distintos chips,
   usar parametro `driver=` y funciones con nombres estandarizados.

3. **Eventos opcionales**: Todo evento debe estar protegido con
   `EMIC:ifdef usedEvent.nombre`. Nunca asumir que el integrador implementa un evento.

4. **Ejecucion no-bloqueante**: El `poll` no debe tardar mas de unos microsegundos.
   Usar maquinas de estados y flags para logica temporal.

5. **Registrar init/poll**: Siempre usar `EMIC:define(inits.X, X)` y
   `EMIC:define(polls.X, X)`. Los drivers/HAL NO registran los suyos.

6. **Discovery completo**: Toda funcion, evento y variable publica debe tener
   comentarios Doxygen con `@fn`/`@var`, `@alias`, `@brief`, `@param`.

7. **Registro de modulo**: Terminar el `.emic` con
   `EMIC:define(main_includes.X, X)` y `EMIC:define(c_modules.X, X)`.

8. **Parametros hacia abajo**: Propagar todos los parametros recibidos del modulo
   hacia los drivers usando `.{param}.`.

9. **Retrocompatibilidad**: Nunca modificar la firma de funciones existentes. Se
   pueden agregar nuevas funciones condicionadas con `EMIC:ifdef`.

10. **Configuradores**: Si la API tiene opciones de usuario, declararlas con
    `EMIC:json(type=configurator)` para que aparezcan en el wizard del editor.

---

## 11. Glosario

| Termino | Definicion |
|---------|-----------|
| **API EMIC** | Capa de abstraccion funcional entre modulo y driver |
| **Driver** | Codigo de control para hardware externo al MCU |
| **HAL** | Hardware Abstraction Layer para perifericos internos del MCU |
| **Hard** | Codigo especifico del microcontrolador (registros, ISR) |
| **Modulo** | Unidad funcional completa (PCB + firmware + configuracion) |
| **Discovery** | Proceso que indexa recursos publicados (@fn, @var, @event) |
| **Configurator** | Wizard generado automaticamente a partir de EMIC:json |
| **program.xml** | Script visual del integrador con logica de negocio |
| **generate.emic** | Script de metaprogramacion que ensambla el proyecto |
| **EMIC:setInput** | Directiva que incluye y procesa recursivamente un archivo |
| **EMIC:copy** | Directiva que copia un archivo con sustitucion de parametros |
| **EMIC:define** | Directiva que define una macro (clave=valor) |
| **EMIC:ifdef** | Compilacion condicional basada en existencia de macro |
| **init** | Funcion de inicializacion ejecutada una vez al arrancar |
| **poll** | Funcion de sondeo ejecutada en cada ciclo del main loop |
| **usedEvent** | Macro definida automaticamente cuando el integrador usa un evento |
| **usedFunction** | Macro definida cuando el integrador usa una funcion |
| **Virtual path** | Ruta logica (`DEV:`, `TARGET:`, `SYS:`, `USER:`) |
