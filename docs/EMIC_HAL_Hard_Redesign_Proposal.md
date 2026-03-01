# Propuesta de Rediseño: Capas HAL y Hard del SDK EMIC

> Documento de propuesta para diseñar las capas HAL (Hardware Abstraction Layer)
> y Hard (Hardware-Specific Implementation) del nuevo SDK EMIC, con el objetivo de soportar
> multiples familias de microcontroladores, exponer informacion estructurada para
> documentacion y agentes de IA mediante procesos EMIC especializados.

---

## Indice

1. [Estado Actual (referencia)](#1-estado-actual-referencia)
2. [Problemas y Limitaciones](#2-problemas-y-limitaciones)
3. [Objetivos del Rediseño](#3-objetivos-del-rediseño)
4. [Arquitectura Propuesta](#4-arquitectura-propuesta)
5. [Procesos EMIC: Parseo Multi-Proposito](#5-procesos-emic-parseo-multi-proposito)
6. [Nuevo Sistema de Metadata: EMIC:json para HAL/Hard](#6-nuevo-sistema-de-metadata-emicjson-para-halhard)
7. [Contrato de Periferico: La Interfaz Estandar](#7-contrato-de-periferico-la-interfaz-estandar)
8. [Descriptor de MCU](#8-descriptor-de-mcu)
9. [Descriptor de Pin Map](#9-descriptor-de-pin-map)
10. [Ejemplos Completos](#10-ejemplos-completos)
11. [Impacto en Capas Superiores](#11-impacto-en-capas-superiores)
12. [Plan de Implementacion](#12-plan-de-implementacion)

---

## 1. Estado Actual (referencia)

> **Nota**: Esta seccion describe el SDK existente como referencia. El nuevo SDK
> se diseña desde cero, sin necesidad de compatibilidad con esta estructura.

### Arquitectura vigente

```
API / Driver
    │
    ▼
_hal/PERIFERICO/periferico.emic          ← Una linea: forwarding puro
    │
    ▼
_hard/{system.ucName}/PERIFERICO/        ← Codigo MCU-especifico
    ├── periferico.emic                  ← Orquestador (copy/define)
    ├── inc/periferico.h                 ← Registros, prototipos
    └── src/periferico.c                 ← Implementacion con SFRs
```

### Caracteristicas del diseño actual

**HAL (`_hal/`)**:
- Cada archivo es un **dispatcher de una linea** que redirige a `_hard/`:
  ```
  EMIC:setInput(DEV:_hard/.{system.ucName}./GPIO/gpio.emic)
  ```
- Pasa parametros verbatim al hard: `port=.{port}., baud=.{baud}.`
- No contiene logica, no valida, no documenta
- No tiene tags EMIC-Codify (Discovery no lo indexa)

**Hard (`_hard/`)**:
- 7 MCUs soportados, todos de la familia Microchip PIC24/dsPIC/PIC32
- Acceso directo a SFRs: `AD1CON1bits.ADON = 1`, `U1BRG = ...`
- Dos patrones de orquestacion: `EMIC:copy` (dsPIC) vs
  `EMIC:setOutput/restoreOutput` (PIC24)
- Pin mapping via archivos individuales `.h` por pin con macros
  (`TRIS_.{name}.`, `PIN_.{name}.`, `LAT_.{name}.`, etc.)
- Sin tags EMIC-Codify — las funciones no son visibles para Discovery
- `system.ucName` definido en el header del PCB (`_pcb/`)

### Perifericos implementados por MCU

| Periferico | pic24FJ64GA002 | pic24FJ128GA010 | pic24FJ128GC006 | dsPIC33EP512MC806 | PIC32MZ2048EFM064 |
|------------|:-:|:-:|:-:|:-:|:-:|
| GPIO | Si | Si | Si | Si | Si |
| ADC | Si | Si | Si | — | — |
| UART | Si | Si | Si | Si | — |
| SPI | Si | Si | Si | Si | — |
| I2C | Si | Si | Si | — | — |
| Timer | Si | Si | Si | Si | — |
| PWM | — | — | — | Si | — |
| Flash | — | — | Si | — | — |
| RefClock | — | — | — | Si | — |
| Change Notif. | — | — | Si | — | — |

---

## 2. Problemas y Limitaciones

### 2.1. Monocultura de microcontroladores

El SDK actual solo soporta la familia Microchip PIC (16/32-bit con compilador
XC16/XC32). Todos los MCUs comparten:
- Arquitectura de registros similar (SFRs, bitfields via `xc.h`)
- Misma convencion de nombres (`TRIS`, `LAT`, `PORT`, `ANSEL`)
- Mismo toolchain (MPLAB X + XC8/XC16/XC32)

**No es posible agregar** MCUs de otras familias (STM32, ESP32, AVR, RISC-V,
RP2040, nRF52, SAMD) sin reescribir la capa hard completa Y cambiar de
toolchain.

### 2.2. HAL sin contrato

La capa HAL actual no define **que funciones debe implementar** cada periferico.
No existe un "contrato" o "interfaz" que diga:

> "Un UART debe exponer: `init()`, `sendByte()`, `readByte()`, `setBaudRate()`,
> y una ISR de recepcion"

La equivalencia entre implementaciones de diferentes MCUs es **implicita**:
funciona porque los desarrolladores nombraron las funciones igual manualmente.
Si un nuevo MCU nombra algo diferente, no hay forma de detectar la
incompatibilidad hasta que la compilacion falla.

### 2.3. Invisibilidad para Discovery y AI

Las capas HAL/Hard no participan del proceso Discovery:
- No tienen tags `@fn`, `@var`, `@event`
- No tienen `EMIC:json` de ningun tipo
- No tienen `EMIC:tag(driverName=...)`

Esto significa que:
- Un agente de IA no puede saber que perifericos tiene un MCU
- No puede saber cuantas UARTs, canales ADC, o timers hay disponibles
- No puede verificar si una API es compatible con el MCU seleccionado
- No puede generar documentacion automatica de capacidades del hardware

### 2.4. Pin mapping sin estructura

Los archivos de pin (`setPinB12.h`) son macros C sin metadata. El sistema no
sabe:
- Que pines existen en un MCU
- Que capacidades tiene cada pin (analogico, digital, remappable, PWM, etc.)
- Que pines ya estan asignados en un PCB
- Que pines quedan libres para un nuevo periferico

### 2.5. Ausencia de informacion sobre capacidades

Cuando una API necesita un recurso del MCU (ej: canal ADC, timer, puerto SPI),
no hay forma programatica de:
- Consultar si el MCU lo soporta
- Saber cuantas instancias hay disponibles
- Verificar que no haya conflictos de recursos (ej: dos APIs usando el mismo timer)

### 2.6. Documentacion manual

Toda la documentacion sobre capacidades del hardware es externa al codigo.
No se puede generar automaticamente un documento que diga: "Este modulo usa
pic24FJ128GC006 con 2 UARTs, 1 I2C, 12 canales ADC, 45 GPIOs" — esa
informacion no esta codificada en el SDK.

---

## 3. Objetivos del Rediseño

| # | Objetivo | Prioridad |
|---|----------|:---------:|
| 1 | **Multi-familia**: Soportar MCUs de distintas arquitecturas (PIC, ARM Cortex-M, ESP32, AVR, RISC-V) con el mismo SDK | Alta |
| 2 | **Contrato de periferico**: Definir interfaces estandar que cada implementacion hard debe cumplir | Alta |
| 3 | **Metadata para AI**: Exponer informacion estructurada sobre MCU, perifericos y pines para agentes de IA y DevAgent | Alta |
| 4 | **Procesos EMIC especializados**: Soportar multiples procesos de parseo (Discovery, HardwareInfo, Validation, Generate) con comportamiento condicionado por macros de sistema | Alta |
| 5 | **Auto-documentacion**: Generar documentacion de capacidades a partir de la metadata del SDK | Media |
| 6 | **Validacion de compatibilidad**: Verificar en compile-time que las APIs/drivers usados son compatibles con el MCU | Media |
| 7 | **Gestion de recursos**: Rastrear asignacion de pines, canales, timers para detectar conflictos | Media |
| 8 | **Toolchain-agnostic**: Separar la abstraccion de periferico del toolchain especifico | Media |

> **Nota**: Este es un SDK nuevo desde cero. No se busca compatibilidad retroactiva
> con el SDK existente.

---

## 4. Arquitectura Propuesta

### Vista general

```
┌─────────────────────────────────────────────────────────────────────┐
│  API / Driver                                                       │
│  Consume perifericos via HAL usando funciones del contrato          │
│  EMIC:setInput(DEV:_hal/UART/UART.emic, port=1, baud=9600)        │
├─────────────────────────────────────────────────────────────────────┤
│  HAL  (_hal/)                              ◄── REDISEÑADA          │
│                                                                     │
│  ┌─────────────────────────────────────┐                           │
│  │ EMIC:json(type = peripheral)        │ ← Contrato del periferico │
│  │ Define: funciones, parametros,      │                           │
│  │ capacidades, tipos, instancias      │                           │
│  ├─────────────────────────────────────┤                           │
│  │ Routing a implementacion hard       │ ← Mantiene patron actual  │
│  │ EMIC:setInput(DEV:_hard/...)        │                           │
│  └─────────────────────────────────────┘                           │
├─────────────────────────────────────────────────────────────────────┤
│  Hard  (_hard/{familia}/{modelo}/)         ◄── REESTRUCTURADA      │
│                                                                     │
│  ┌─────────────────────────────────────┐                           │
│  │ EMIC:json(type = mcu)              │ ← Descriptor del MCU      │
│  │ Define: familia, nucleos, memoria,  │                           │
│  │ perifericos disponibles, pines      │                           │
│  ├─────────────────────────────────────┤                           │
│  │ Implementaciones de periferico      │ ← Codigo MCU-especifico   │
│  │ Cumple contrato definido en HAL     │                           │
│  ├─────────────────────────────────────┤                           │
│  │ EMIC:json(type = pin_map)          │ ← Descriptor de pines     │
│  │ Capacidades de cada pin fisico      │                           │
│  └─────────────────────────────────────┘                           │
├─────────────────────────────────────────────────────────────────────┤
│  PCB  (_pcb/)                              ← Sin cambios           │
│  Define system.ucName, asigna pines con nombre logico               │
└─────────────────────────────────────────────────────────────────────┘
```

### Nuevo arbol de directorios `_hard/`

El cambio estructural mas importante es organizar `_hard/` por **familia**
antes que por modelo. Esto refleja que los MCUs de la misma familia comparten
mucho codigo base:

```
_hard/
├── Microchip/
│   ├── PIC24F/                          ← Familia
│   │   ├── PIC24F.mcu.emic             ← Descriptor de familia
│   │   ├── pic24FJ64GA002/             ← Modelo
│   │   │   ├── mcu.emic                ← Descriptor del MCU
│   │   │   ├── pins/
│   │   │   │   └── pin_map.emic        ← Mapa de pines con metadata
│   │   │   ├── ADC/
│   │   │   │   ├── adc.emic
│   │   │   │   ├── inc/adc.h
│   │   │   │   └── src/adc.c
│   │   │   ├── GPIO/
│   │   │   ├── I2C/
│   │   │   ├── SPI/
│   │   │   ├── System/
│   │   │   ├── Timer/
│   │   │   └── UART/
│   │   ├── pic24FJ128GA010/
│   │   │   └── ... (hereda base de PIC24F, sobreescribe diferencias)
│   │   └── pic24FJ128GC006/
│   │       └── ...
│   ├── dsPIC33/
│   │   ├── dsPIC33.mcu.emic
│   │   └── dsPIC33EP512MC806/
│   │       └── ...
│   └── PIC32MZ/
│       └── ...
├── ST/
│   ├── STM32F1/
│   │   ├── STM32F1.mcu.emic
│   │   └── STM32F103C8/               ← "Blue Pill"
│   │       ├── mcu.emic
│   │       ├── pins/pin_map.emic
│   │       ├── GPIO/
│   │       ├── UART/
│   │       ├── SPI/
│   │       ├── I2C/
│   │       ├── ADC/
│   │       ├── Timer/
│   │       └── System/
│   ├── STM32F4/
│   │   └── STM32F407VG/
│   └── STM32G0/
│       └── STM32G071RB/
├── Espressif/
│   ├── ESP32/
│   │   └── ESP32-WROOM-32/
│   └── ESP32S3/
│       └── ESP32-S3-WROOM-1/
├── Nordic/
│   └── nRF52/
│       └── nRF52840/
├── Atmel/
│   └── AVR/
│       ├── ATmega328P/
│       └── ATmega2560/
└── RaspberryPi/
    └── RP2/
        └── RP2040/
```

### Resolucion de ruta: triple macro `system.ucVendor` / `system.ucFamily` / `system.ucName`

El PCB define tres macros obligatorias:

```
EMIC:define(system.ucVendor, Microchip)
EMIC:define(system.ucFamily, PIC24F)
EMIC:define(system.ucName, pic24FJ128GC006)
```

El HAL usa la ruta completa para resolver la implementacion hard:

```
EMIC:setInput(DEV:_hard/.{system.ucVendor}./.{system.ucFamily}./.{system.ucName}./UART/UART.emic, ...)
```

Las tres macros son obligatorias en el nuevo SDK. No existen rutas alternativas
ni fallbacks — la estructura de directorios es unica y consistente.

---

## 5. Procesos EMIC: Parseo Multi-Proposito

### Concepto

En el SDK actual, existen dos procesos de parseo principales:
- **Discovery**: Parsea archivos `.emic` y `.h` para extraer recursos publicados
  (funciones, variables, eventos) → alimenta el sidebar del Editor EMIC
- **Generate**: Parsea los mismos archivos para producir codigo C compilable →
  genera los archivos en `TARGET:`

El nuevo SDK introduce la posibilidad de definir **procesos EMIC adicionales**,
cada uno con objetivos diferentes. Todos los procesos usan el mismo motor de
parseo (EMIC-Codify) pero actuan sobre distintos archivos y extraen
informacion distinta.

### Mecanismo: Macros de sistema como discriminador

Cuando el sistema inicia un proceso de parseo, define automaticamente una
**macro de sistema** que identifica el proceso activo. Esta macro se puede
usar con `EMIC:ifdef` para que un mismo archivo `.emic` se comporte de
forma diferente segun quien lo este parseando.

```
// El sistema define automaticamente al iniciar cada proceso:
// system.process.Discovery    → definida cuando corre Discovery
// system.process.HardwareInfo → definida cuando corre HardwareInfo
// system.process.Validation   → definida cuando corre Validation
// system.process.Generate     → definida cuando corre Generate
```

### Procesos propuestos para HAL/Hard

| Proceso | Parsea | Objetivo | Resultado |
|---------|--------|----------|-----------|
| **Discovery** | `_api/`, `_drivers/` | Publicar recursos al integrador | Sidebar del Editor (funciones, variables, eventos) |
| **HardwareInfo** | `_hard/`, `_hal/` | Extraer metadata de MCU, perifericos y pines | Catalogo de hardware para AI y documentacion |
| **Validation** | `_hal/` + proyecto | Verificar compatibilidad API↔MCU | Reporte de errores/warnings |
| **Generate** | Todo | Producir codigo C compilable | Archivos `.c` y `.h` en `TARGET:` |
| **PinInfo** | `_hard/*/pins/` | Extraer mapa de pines con capacidades | Descriptor de pines para PCB design |

### Ejemplo: archivo dual con `EMIC:ifdef`

Un archivo `_hard/ST/STM32F1/STM32F103C8/mcu.emic` puede comportarse
de forma diferente segun el proceso que lo parsea:

```
// ================================================================
// SECCION 1: Siempre se ejecuta (metadata comun)
// ================================================================
EMIC:json(type = mcu)
{
    "vendor": "ST",
    "family": "STM32F1",
    "model": "STM32F103C8",
    "brief": "ARM Cortex-M3, 72MHz, 64KB Flash, 20KB SRAM",
    "peripherals": {
        "UART": { "available": true, "instances": 3 },
        "SPI":  { "available": true, "instances": 2 },
        "ADC":  { "available": true, "channels": 10 }
    }
}

// ================================================================
// SECCION 2: Solo se ejecuta durante HardwareInfo
// Expone informacion detallada de registros y capacidades
// ================================================================
EMIC:ifdef system.process.HardwareInfo

EMIC:json(type = mcu_detail)
{
    "memory_map": {
        "flash_start": "0x08000000",
        "ram_start": "0x20000000",
        "peripheral_start": "0x40000000"
    },
    "clock_tree": {
        "hse_range_mhz": [4, 16],
        "pll_multipliers": [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16],
        "ahb_prescalers": [1, 2, 4, 8, 16, 64, 128, 256, 512],
        "apb1_max_mhz": 36,
        "apb2_max_mhz": 72
    },
    "dma_channels": [
        {"channel": 1, "peripherals": ["ADC1", "TIM2_CH3", "TIM4_CH1"]},
        {"channel": 2, "peripherals": ["SPI1_RX", "USART3_TX", "TIM1_CH1"]},
        {"channel": 3, "peripherals": ["SPI1_TX", "USART3_RX", "TIM1_CH2"]}
    ]
}

EMIC:endif

// ================================================================
// SECCION 3: Solo se ejecuta durante Validation
// Define reglas de validacion especificas del MCU
// ================================================================
EMIC:ifdef system.process.Validation

EMIC:json(type = validation_rules)
{
    "constraints": [
        {
            "rule": "max_uart_baud",
            "value": 4500000,
            "message": "STM32F103 UART max baud rate is 4.5 Mbps"
        },
        {
            "rule": "adc_max_channels_simultaneous",
            "value": 2,
            "message": "STM32F103 has 2 ADC units for dual mode"
        },
        {
            "rule": "apb1_clock_limit",
            "value": 36,
            "unit": "MHz",
            "message": "APB1 peripherals (UART2/3, SPI2, I2C, TIM2-7) max 36 MHz"
        }
    ]
}

EMIC:endif

// ================================================================
// SECCION 4: Solo se ejecuta durante Generate
// Configura macros y includes para compilacion
// ================================================================
EMIC:ifdef system.process.Generate

EMIC:define(system.mcu_header, stm32f1xx.h)
EMIC:define(system.fcy_formula, HCLK)
EMIC:define(system.startup_file, startup_stm32f103c8tx.s)
EMIC:define(system.linker_script, STM32F103C8Tx_FLASH.ld)
EMIC:define(system.compiler, arm-none-eabi-gcc)
EMIC:define(system.arch_flags, -mcpu=cortex-m3 -mthumb)

EMIC:endif
```

### Ejemplo: contrato de periferico con secciones por proceso

Un archivo `_hal/UART/UART.emic` tambien puede tener secciones condicionales:

```
// ================================================================
// CONTRATO (siempre visible — base para todos los procesos)
// ================================================================
EMIC:json(type = peripheral)
{
    "name": "UART",
    "brief": "Universal Asynchronous Receiver-Transmitter",
    "category": "Communication",
    "requires": {
        "functions": [
            {"name": "UART{port}_init", "signature": "void UART{port}_init(void)"},
            {"name": "UART{port}_sendByte", "signature": "void UART{port}_sendByte(uint8_t data)"},
            {"name": "UART{port}_readByte", "signature": "uint8_t UART{port}_readByte(void)"},
            {"name": "UART{port}_dataAvailable", "signature": "uint8_t UART{port}_dataAvailable(void)"}
        ]
    }
}

// ================================================================
// VALIDATION: verifica que el MCU soporta UART
// ================================================================
EMIC:ifdef system.process.Validation
    // El validador lee el contrato JSON de arriba y lo cruza con
    // el descriptor MCU para verificar compatibilidad
    EMIC:json(type = validation_check)
    {
        "check": "peripheral_available",
        "peripheral": "UART",
        "severity": "error",
        "message": "El MCU seleccionado no soporta UART"
    }
EMIC:endif

// ================================================================
// GENERATE: incluye la implementacion hard
// ================================================================
EMIC:ifdef system.process.Generate
    EMIC:setInput(DEV:_hard/.{system.ucVendor}./.{system.ucFamily}./.{system.ucName}./UART/UART.emic,port=.{port}.,BufferSize=.{BufferSize}.,baud=.{baud}.,driver=.{driver}.)
EMIC:endif
```

### Ventajas del modelo multi-proceso

| Aspecto | Sin procesos (actual) | Con procesos EMIC (propuesto) |
|---------|----------------------|------------------------------|
| **Un archivo, un proposito** | Cada archivo solo sirve para Generate o Discovery | Un archivo puede servir para multiples propositos |
| **Informacion de hardware** | No accesible programaticamente | Proceso HardwareInfo extrae toda la metadata |
| **Validacion** | Solo en compile-time (errores de C) | Pre-validacion EMIC antes de generar codigo |
| **Documentacion** | Manual, externa al codigo | Auto-generada desde metadata de HardwareInfo |
| **Condicionalidad** | Solo `EMIC:ifdef` sobre macros de config | `EMIC:ifdef system.process.X` controla que se expone |
| **Extensibilidad** | Agregar info requiere crear archivos nuevos | Agregar secciones `EMIC:ifdef` en archivos existentes |

### Flujo de ejecucion de cada proceso

```
┌─────────────────────────────────────────────────────────────────────┐
│  Proceso HardwareInfo                                               │
│                                                                     │
│  1. Sistema define: system.process.HardwareInfo                     │
│  2. Parsea: _hard/{vendor}/{family}/{model}/mcu.emic                │
│     → Extrae EMIC:json(type = mcu) + secciones ifdef HardwareInfo   │
│  3. Parsea: _hard/{vendor}/{family}/{model}/pins/pin_map.emic       │
│     → Extrae EMIC:json(type = pin_map)                              │
│  4. Parsea: _hal/*/periferico.emic                                  │
│     → Extrae EMIC:json(type = peripheral) como contratos            │
│  5. Resultado: Catalogo completo de hardware en JSON                │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  Proceso Validation                                                 │
│                                                                     │
│  1. Sistema define: system.process.Validation + macros del proyecto │
│  2. Parsea: _hal/*/periferico.emic                                  │
│     → Lee contratos + secciones ifdef Validation                    │
│  3. Cruza contratos con descriptor MCU del proyecto                 │
│  4. Resultado: Reporte de compatibilidad (errores/warnings)         │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  Proceso PinInfo                                                    │
│                                                                     │
│  1. Sistema define: system.process.PinInfo                          │
│  2. Parsea: _hard/{vendor}/{family}/{model}/pins/pin_map.emic       │
│     → Extrae mapa completo de pines con capacidades                 │
│  3. Parsea: _pcb/{pcbName}/pcb.emic                                 │
│     → Extrae asignaciones de pines del PCB                          │
│  4. Resultado: Mapa de pines disponibles/asignados                  │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 6. Nuevo Sistema de Metadata: EMIC:json para HAL/Hard

### Tipos de metadata propuestos

Los tags actuales de EMIC-Codify (`@fn`, `@var`, `@event`, `EMIC:tag`) estan
diseñados para **publicar recursos al integrador** via Discovery → Editor.
Las capas HAL/Hard no publican recursos al integrador; son consumidas
internamente por APIs y drivers.

Se propone un nuevo uso de `EMIC:json` con tres tipos especificos para
HAL/Hard:

| Tipo | Archivo | Proposito |
|------|---------|-----------|
| `EMIC:json(type = peripheral)` | `_hal/PERIFERICO/periferico.emic` | Define el **contrato**: que funciones debe implementar cualquier MCU para este periferico |
| `EMIC:json(type = mcu)` | `_hard/{vendor}/{family}/{model}/mcu.emic` | Describe el **MCU**: familia, nucleos, memoria, perifericos disponibles, toolchain |
| `EMIC:json(type = pin_map)` | `_hard/{vendor}/{family}/{model}/pins/pin_map.emic` | Describe los **pines**: nombre fisico, capacidades, funciones alternativas |

Estos bloques JSON son procesados por los procesos EMIC especializados
(HardwareInfo, Validation) — **no generan entradas en el sidebar del
integrador**. En cambio, alimentan:
- El DevAgent y otros agentes de IA (para seleccion de MCU, validacion, generacion de codigo)
- El sistema de documentacion automatica
- El validador de compatibilidad (API requiere ADC → MCU tiene ADC?)
- El gestor de recursos (pin X ya asignado → conflicto)

---

## 7. Contrato de Periferico: La Interfaz Estandar

### Concepto

Un **contrato de periferico** define:
- Que funciones debe implementar la capa hard para ese periferico
- Que parametros acepta la invocacion HAL
- Que capacidades son obligatorias y cuales opcionales
- Que tipos de datos usa

El contrato NO contiene codigo — es metadata pura. El codigo sigue viviendo
en `_hard/`. El contrato permite al sistema verificar que la implementacion
hard de un MCU cumple con lo que las APIs esperan.

### Estructura: `EMIC:json(type = peripheral)`

```javascript
EMIC:json(type = peripheral)
{
    // ──────────────────────────────────────────────
    // IDENTIFICACION
    // ──────────────────────────────────────────────
    "name": "UART",
    "brief": "Universal Asynchronous Receiver-Transmitter",
    "description": "Comunicacion serial asincrona full-duplex con
                    buffers de transmision y recepcion.",
    "category": "Communication",

    // ──────────────────────────────────────────────
    // PARAMETROS DE INVOCACION
    // Estos son los parametros que recibe EMIC:setInput
    // cuando una API o driver incluye este periferico
    // ──────────────────────────────────────────────
    "parameters": [
        {
            "name": "port",
            "type": "uint8_t",
            "required": true,
            "brief": "Numero de puerto UART (1, 2, ...)",
            "determines_instance": true
        },
        {
            "name": "baud",
            "type": "uint32_t",
            "required": true,
            "default": "9600",
            "brief": "Velocidad en baudios"
        },
        {
            "name": "BufferSize",
            "type": "uint16_t",
            "required": false,
            "default": "64",
            "brief": "Tamaño del buffer de recepcion en bytes"
        },
        {
            "name": "driver",
            "type": "pin_name",
            "required": true,
            "brief": "Nombre del grupo de pines TX/RX (definido en PCB)"
        }
    ],

    // ──────────────────────────────────────────────
    // CONTRATO: FUNCIONES OBLIGATORIAS
    // Toda implementacion hard DEBE proveer estas funciones.
    // Los nombres usan {port} como placeholder de instancia.
    // ──────────────────────────────────────────────
    "requires": {
        "functions": [
            {
                "name": "UART{port}_init",
                "signature": "void UART{port}_init(void)",
                "brief": "Inicializa el periferico UART",
                "category": "lifecycle"
            },
            {
                "name": "UART{port}_bd",
                "signature": "void UART{port}_bd(uint32_t baudRate)",
                "brief": "Configura la velocidad de comunicacion",
                "category": "config"
            },
            {
                "name": "UART{port}_sendByte",
                "signature": "void UART{port}_sendByte(uint8_t data)",
                "brief": "Envia un byte por el puerto",
                "category": "io"
            },
            {
                "name": "UART{port}_readByte",
                "signature": "uint8_t UART{port}_readByte(void)",
                "brief": "Lee un byte del buffer de recepcion",
                "category": "io"
            },
            {
                "name": "UART{port}_dataAvailable",
                "signature": "uint8_t UART{port}_dataAvailable(void)",
                "brief": "Retorna 1 si hay datos disponibles en el buffer",
                "category": "io"
            }
        ],
        "interrupts": [
            {
                "name": "UART{port}_RX_ISR",
                "brief": "ISR de recepcion: almacena byte en buffer circular",
                "vector": "vendor_specific"
            }
        ]
    },

    // ──────────────────────────────────────────────
    // CONTRATO: FUNCIONES OPCIONALES
    // La implementacion hard PUEDE proveer estas funciones.
    // Si no las provee, la API debe manejar su ausencia.
    // ──────────────────────────────────────────────
    "optional": {
        "functions": [
            {
                "name": "UART{port}_sendString",
                "signature": "void UART{port}_sendString(const char* str)",
                "brief": "Envia una cadena completa",
                "category": "io",
                "fallback": "Iterar sendByte sobre cada caracter"
            },
            {
                "name": "UART{port}_flush",
                "signature": "void UART{port}_flush(void)",
                "brief": "Espera hasta que el buffer TX este vacio",
                "category": "io"
            },
            {
                "name": "UART{port}_setCallback",
                "signature": "void UART{port}_setCallback(void (*cb)(uint8_t))",
                "brief": "Registra callback de recepcion en lugar de buffer",
                "category": "config"
            }
        ],
        "features": [
            {
                "name": "hardware_flow_control",
                "brief": "Soporte para RTS/CTS por hardware"
            },
            {
                "name": "dma_support",
                "brief": "Transferencia por DMA para bloques de datos"
            },
            {
                "name": "9bit_mode",
                "brief": "Modo de 9 bits para protocolos multidrop"
            }
        ]
    },

    // ──────────────────────────────────────────────
    // DEPENDENCIAS
    // Otros perifericos HAL que este periferico necesita
    // ──────────────────────────────────────────────
    "dependencies": [
        {
            "peripheral": "GPIO",
            "reason": "Configuracion de pines TX/RX",
            "mandatory": true
        },
        {
            "peripheral": "System",
            "reason": "FCY para calculo de baud rate",
            "mandatory": true
        }
    ],

    // ──────────────────────────────────────────────
    // STREAMS
    // Integracion con el sistema de streams EMIC
    // ──────────────────────────────────────────────
    "streams": {
        "provides_streamOut": true,
        "provides_streamIn": true,
        "stream_data_type": "uint8_t"
    }
}
```

### Contratos propuestos para perifericos comunes

| Periferico | Funciones obligatorias | Funciones opcionales | Parametros clave |
|------------|----------------------|---------------------|-----------------|
| **GPIO** | `setOutput()`, `setInput()`, `read()`, `write()`, `toggle()` | `setPullUp()`, `setPullDown()`, `setOpenDrain()`, `attachInterrupt()` | `pin` |
| **UART** | `init()`, `bd()`, `sendByte()`, `readByte()`, `dataAvailable()` | `sendString()`, `flush()`, `setCallback()` | `port`, `baud`, `BufferSize`, `driver` |
| **SPI** | `init()`, `transfer()`, `writeByte()`, `readByte()` | `transferDMA()`, `setSpeed()`, `setMode()` | `port`, `configuracion`, `mode` |
| **I2C** | `init()`, `start()`, `stop()`, `writeByte()`, `readByte()`, `ack()`, `nack()` | `writeBlock()`, `readBlock()`, `setSpeed()`, `scanBus()` | `port`, `speed`, `client` |
| **ADC** | `init()`, `addChannel()`, `read()`, `poll()` | `startContinuous()`, `stopContinuous()`, `setResolution()`, `calibrate()` | `resolution`, `referenceVoltage` |
| **Timer** | `init()`, `start()`, `stop()`, `setPeriod()`, `getCount()` | `setPrescaler()`, `setCallback()`, `pwmMode()` | `timer_number`, `period` |
| **PWM** | `init()`, `setDuty()`, `setFrequency()`, `start()`, `stop()` | `setDeadTime()`, `complementaryMode()`, `faultInput()` | `channel`, `frequency`, `resolution` |
| **System** | `initSystem()`, `getClockFrequency()` | `enterSleep()`, `enterIdle()`, `resetDevice()`, `enableWDT()` | — |

---

## 8. Descriptor de MCU

### Estructura: `EMIC:json(type = mcu)`

Este bloque se ubica en el archivo `mcu.emic` de cada modelo de MCU.
Describe completamente las capacidades del microcontrolador:

```javascript
EMIC:json(type = mcu)
{
    // ──────────────────────────────────────────────
    // IDENTIFICACION
    // ──────────────────────────────────────────────
    "vendor": "Microchip",
    "family": "PIC24F",
    "model": "pic24FJ128GC006",
    "brief": "16-bit PIC24F con 128KB Flash, ADC 16-bit, USB, SPI, I2C",
    "architecture": "MIPS16",
    "bits": 16,
    "core": "PIC24",

    // ──────────────────────────────────────────────
    // TOOLCHAIN
    // ──────────────────────────────────────────────
    "toolchain": {
        "compiler": "XC16",
        "ide": "MPLAB X",
        "header": "<xc.h>",
        "programmer": "PICkit3",
        "linker_script": "p24FJ128GC006.gld"
    },

    // ──────────────────────────────────────────────
    // MEMORIA
    // ──────────────────────────────────────────────
    "memory": {
        "flash_kb": 128,
        "ram_kb": 8,
        "eeprom_bytes": 0,
        "flash_page_size": 1024,
        "flash_row_size": 64
    },

    // ──────────────────────────────────────────────
    // RELOJ
    // ──────────────────────────────────────────────
    "clock": {
        "max_frequency_mhz": 32,
        "internal_oscillator_mhz": 8,
        "pll_available": true,
        "fcy_formula": "FOSC / 2"
    },

    // ──────────────────────────────────────────────
    // PERIFERICOS DISPONIBLES
    // Cada entrada referencia un contrato de _hal/ y
    // especifica cuantas instancias tiene este MCU
    // ──────────────────────────────────────────────
    "peripherals": {
        "GPIO": {
            "available": true,
            "total_pins": 53,
            "ports": ["A", "B", "C", "D", "E", "F", "G"],
            "max_5v_tolerant": false
        },
        "UART": {
            "available": true,
            "instances": 4,
            "ports": [1, 2, 3, 4],
            "max_baud": 1000000,
            "features": ["9bit_mode"]
        },
        "SPI": {
            "available": true,
            "instances": 3,
            "ports": [1, 2, 3],
            "max_speed_mhz": 8,
            "modes": ["master", "slave"],
            "features": ["dma_support"]
        },
        "I2C": {
            "available": true,
            "instances": 2,
            "ports": [1, 2],
            "max_speed_khz": 400,
            "features": ["multi_master", "10bit_address"]
        },
        "ADC": {
            "available": true,
            "channels": 12,
            "resolution_bits": 16,
            "reference": "internal",
            "sample_rate_ksps": 200,
            "features": ["differential", "scan_mode"]
        },
        "Timer": {
            "available": true,
            "instances": 5,
            "timers": [1, 2, 3, 4, 5],
            "bits": [16, 16, 16, 16, 16],
            "features": ["32bit_pair", "gate_mode"]
        },
        "PWM": {
            "available": false
        },
        "Flash": {
            "available": true,
            "self_write": true,
            "features": ["row_erase", "row_program"]
        },
        "Input_change_notification": {
            "available": true,
            "channels": 22,
            "features": ["per_pin_enable", "pullup"]
        },
        "USB": {
            "available": true,
            "type": "device",
            "speed": "full_speed"
        }
    },

    // ──────────────────────────────────────────────
    // REMAPPABLE PINS (PPS)
    // ──────────────────────────────────────────────
    "pin_remapping": {
        "available": true,
        "system": "PPS",
        "remap_inputs": ["U1RX", "U2RX", "SDI1", "SDI2", "SCK1IN", "T2CK", "IC1"],
        "remap_outputs": ["U1TX", "U2TX", "SDO1", "SDO2", "SCK1OUT", "OC1", "C1OUT"]
    },

    // ──────────────────────────────────────────────
    // INTERRUPCIONES
    // ──────────────────────────────────────────────
    "interrupts": {
        "total_vectors": 118,
        "priority_levels": 7,
        "nesting": true
    }
}
```

### Uso por el sistema

El descriptor MCU permite responder preguntas programaticamente:

| Pregunta | Respuesta via JSON |
|----------|-------------------|
| Tiene ADC este MCU? | `peripherals.ADC.available == true` |
| Cuantas UARTs? | `peripherals.UART.instances` → 4 |
| Soporta PWM? | `peripherals.PWM.available` → false |
| Cuantos pines GPIO? | `peripherals.GPIO.total_pins` → 53 |
| Que compilador usa? | `toolchain.compiler` → "XC16" |
| Cuanta RAM tiene? | `memory.ram_kb` → 8 |
| Frecuencia maxima? | `clock.max_frequency_mhz` → 32 |

---

## 9. Descriptor de Pin Map

### Estructura: `EMIC:json(type = pin_map)`

El pin map describe todos los pines fisicos del MCU con sus capacidades.
Reemplaza la coleccion de archivos `setPinXX.h` individuales con un
descriptor estructurado que sirve como fuente de verdad y permite
generar las macros C automaticamente:

```javascript
EMIC:json(type = pin_map)
{
    "mcu": "pic24FJ128GC006",
    "package": "TQFP-64",
    "total_io_pins": 53,

    "pins": [
        {
            "id": "RA0",
            "port": "A",
            "bit": 0,
            "pin_number": 17,
            "capabilities": ["digital_io", "analog_input", "change_notification"],
            "analog_channel": 0,
            "cn_number": 2,
            "remappable": false,
            "adc_buffer_index": 0,
            "registers": {
                "tris": "_TRISA0",
                "port": "_RA0",
                "lat": "_LATA0",
                "odc": "_ODA0",
                "ansel": "_ANSA0",
                "cnpu": "_CNPUA0",
                "cnpd": "_CNPDA0"
            }
        },
        {
            "id": "RB12",
            "port": "B",
            "bit": 12,
            "pin_number": 27,
            "capabilities": ["digital_io", "analog_input", "change_notification",
                             "remappable_io"],
            "analog_channel": 12,
            "cn_number": 14,
            "remappable": true,
            "rpin_number": 12,
            "rpout_register": "RPOR6bits.RP12R",
            "adc_buffer_index": 12,
            "registers": {
                "tris": "_TRISB12",
                "port": "_RB12",
                "lat": "_LATB12",
                "odc": "_ODB12",
                "ansel": "_ANSB12"
            }
        },
        {
            "id": "RD1",
            "port": "D",
            "bit": 1,
            "pin_number": 49,
            "capabilities": ["digital_io", "remappable_io"],
            "remappable": true,
            "rpin_number": 24,
            "rpout_register": "RPOR12bits.RP24R",
            "registers": {
                "tris": "_TRISD1",
                "port": "_RD1",
                "lat": "_LATD1",
                "odc": "_ODD1"
            }
        }
        // ... todos los pines del MCU ...
    ],

    // ──────────────────────────────────────────────
    // PINES ESPECIALES (alimentacion, reset, etc.)
    // ──────────────────────────────────────────────
    "special_pins": [
        {"pin_number": 1,  "function": "MCLR",  "type": "reset"},
        {"pin_number": 10, "function": "VDD",   "type": "power"},
        {"pin_number": 11, "function": "VSS",   "type": "ground"},
        {"pin_number": 25, "function": "AVDD",  "type": "analog_power"},
        {"pin_number": 26, "function": "AVSS",  "type": "analog_ground"},
        {"pin_number": 39, "function": "OSC1",  "type": "oscillator"},
        {"pin_number": 40, "function": "OSC2",  "type": "oscillator"}
    ],

    // ──────────────────────────────────────────────
    // FUNCIONES ALTERNATIVAS
    // Mapeo de funciones de periferico a pines fijos
    // (funciones que NO son remappable)
    // ──────────────────────────────────────────────
    "fixed_functions": [
        {"function": "SDA1", "pin": "RG3", "peripheral": "I2C", "port": 1},
        {"function": "SCL1", "pin": "RG2", "peripheral": "I2C", "port": 1},
        {"function": "SDA2", "pin": "RA3", "peripheral": "I2C", "port": 2},
        {"function": "SCL2", "pin": "RA2", "peripheral": "I2C", "port": 2}
    ]
}
```

### Generacion de macros C desde pin_map

El sistema genera automaticamente los archivos `setPinXX.h` a partir del
pin map, usando el formato estandar de macros:

```
// Generado automaticamente desde pin_map.emic
// Pin: RB12 (pin fisico 27)
// Capacidades: digital_io, analog_input, change_notification, remappable_io

#define TRIS_.{name}.       _TRISB12
#define PORT_.{name}.       _RB12
#define LAT_.{name}.        _LATB12
#define ODC_.{name}.        _ODB12
#define PIN_.{name}.        _RB12
#define RPOUT_.{name}.      RPOR6bits.RP12R
#define RPIN_.{name}.       12
#define CN_.{name}.         14
#define ADC_value_.{name}.  Buffer_entradas[12]
#define HAL_SetAnalog_.{name}.()  {_ANSB12=1; adc_addAnalogChannel(12);}
```

La ventaja: el pin map es la **fuente de verdad**. Los archivos `setPinXX.h`
se generan de forma determinista. Si se agrega un MCU nuevo, solo se crea
el `pin_map.emic` con el JSON y el sistema genera todos los `.h`.

---

## 10. Ejemplos Completos

### 10.1. UART HAL con contrato

**`_hal/UART/UART.emic`** (rediseñado):
```
// ================================================================
// CONTRATO DEL PERIFERICO UART
// ================================================================
EMIC:json(type = peripheral)
{
    "name": "UART",
    "brief": "Universal Asynchronous Receiver-Transmitter",
    "description": "Comunicacion serial asincrona full-duplex con
                    buffers de transmision y recepcion.",
    "category": "Communication",
    "parameters": [
        {
            "name": "port",
            "type": "uint8_t",
            "required": true,
            "brief": "Numero de puerto UART",
            "determines_instance": true
        },
        {
            "name": "baud",
            "type": "uint32_t",
            "required": true,
            "default": "9600",
            "brief": "Velocidad en baudios"
        },
        {
            "name": "BufferSize",
            "type": "uint16_t",
            "required": false,
            "default": "64",
            "brief": "Tamaño del buffer de recepcion"
        },
        {
            "name": "driver",
            "type": "pin_name",
            "required": true,
            "brief": "Nombre del grupo de pines TX/RX"
        }
    ],
    "requires": {
        "functions": [
            {
                "name": "UART{port}_init",
                "signature": "void UART{port}_init(void)",
                "brief": "Inicializa el periferico UART",
                "category": "lifecycle"
            },
            {
                "name": "UART{port}_bd",
                "signature": "void UART{port}_bd(uint32_t baudRate)",
                "brief": "Configura la velocidad",
                "category": "config"
            },
            {
                "name": "UART{port}_sendByte",
                "signature": "void UART{port}_sendByte(uint8_t data)",
                "brief": "Envia un byte",
                "category": "io"
            },
            {
                "name": "UART{port}_readByte",
                "signature": "uint8_t UART{port}_readByte(void)",
                "brief": "Lee un byte del buffer",
                "category": "io"
            },
            {
                "name": "UART{port}_dataAvailable",
                "signature": "uint8_t UART{port}_dataAvailable(void)",
                "brief": "Retorna 1 si hay datos disponibles",
                "category": "io"
            }
        ]
    },
    "optional": {
        "functions": [
            {
                "name": "UART{port}_sendString",
                "signature": "void UART{port}_sendString(const char* str)",
                "brief": "Envia una cadena completa",
                "category": "io"
            }
        ],
        "features": ["hardware_flow_control", "dma_support", "9bit_mode"]
    },
    "dependencies": [
        {"peripheral": "GPIO", "reason": "Configuracion de pines TX/RX"},
        {"peripheral": "System", "reason": "FCY para calculo de baud rate"}
    ],
    "streams": {
        "provides_streamOut": true,
        "provides_streamIn": true,
        "stream_data_type": "uint8_t"
    }
}

// ================================================================
// ROUTING A IMPLEMENTACION HARD (solo durante Generate)
// ================================================================
EMIC:ifdef system.process.Generate
    EMIC:setInput(DEV:_hard/.{system.ucVendor}./.{system.ucFamily}./.{system.ucName}./UART/UART.emic,port=.{port}.,BufferSize=.{BufferSize}.,baud=.{baud}.,driver=.{driver}.)
EMIC:endif
```

### 10.2. ADC HAL con contrato

**`_hal/ADC/adc.emic`** (rediseñado):
```
EMIC:json(type = peripheral)
{
    "name": "ADC",
    "brief": "Analog-to-Digital Converter",
    "description": "Conversion analogico-digital con soporte para
                    multiples canales, escaneo automatico y buffer.",
    "category": "Analog",
    "parameters": [],
    "requires": {
        "functions": [
            {
                "name": "adc_init",
                "signature": "void adc_init(void)",
                "brief": "Inicializa el modulo ADC",
                "category": "lifecycle"
            },
            {
                "name": "adc_addAnalogChannel",
                "signature": "void adc_addAnalogChannel(uint8_t channel)",
                "brief": "Agrega un canal al escaneo",
                "category": "config"
            },
            {
                "name": "poll_adc",
                "signature": "void poll_adc(void)",
                "brief": "Ejecuta conversion y actualiza buffers",
                "category": "lifecycle"
            }
        ],
        "variables": [
            {
                "name": "Buffer_entradas",
                "type": "int16_t[]",
                "brief": "Buffer con el ultimo valor de cada canal"
            }
        ]
    },
    "optional": {
        "functions": [
            {
                "name": "adc_setResolution",
                "signature": "void adc_setResolution(uint8_t bits)",
                "brief": "Configura la resolucion (8, 10, 12, 16 bits)"
            },
            {
                "name": "adc_readChannel",
                "signature": "uint16_t adc_readChannel(uint8_t channel)",
                "brief": "Lee un canal individual de forma sincrona"
            }
        ]
    },
    "dependencies": [
        {"peripheral": "GPIO", "reason": "Configuracion de pines analogicos"}
    ]
}

EMIC:ifndef _HAL_ADC_EMIC_
EMIC:define(_HAL_ADC_EMIC_,true)

EMIC:ifdef system.process.Generate
    EMIC:setInput(DEV:_hard/.{system.ucVendor}./.{system.ucFamily}./.{system.ucName}./ADC/adc.emic)
EMIC:endif

EMIC:endif
```

### 10.3. MCU descriptor para STM32F103

**`_hard/ST/STM32F1/STM32F103C8/mcu.emic`** (ejemplo ARM):
```
EMIC:json(type = mcu)
{
    "vendor": "ST",
    "family": "STM32F1",
    "model": "STM32F103C8",
    "brief": "ARM Cortex-M3, 72MHz, 64KB Flash, 20KB SRAM",
    "architecture": "ARM",
    "core": "Cortex-M3",
    "bits": 32,

    "toolchain": {
        "compiler": "arm-none-eabi-gcc",
        "ide": "STM32CubeIDE",
        "header": "stm32f1xx.h",
        "programmer": "ST-Link V2",
        "linker_script": "STM32F103C8Tx_FLASH.ld"
    },

    "memory": {
        "flash_kb": 64,
        "ram_kb": 20,
        "eeprom_bytes": 0,
        "flash_page_size": 1024
    },

    "clock": {
        "max_frequency_mhz": 72,
        "internal_oscillator_mhz": 8,
        "pll_available": true,
        "fcy_formula": "HCLK"
    },

    "peripherals": {
        "GPIO": {
            "available": true,
            "total_pins": 37,
            "ports": ["A", "B", "C"],
            "max_5v_tolerant": true
        },
        "UART": {
            "available": true,
            "instances": 3,
            "ports": [1, 2, 3],
            "max_baud": 4500000,
            "features": ["dma_support", "hardware_flow_control"]
        },
        "SPI": {
            "available": true,
            "instances": 2,
            "ports": [1, 2],
            "max_speed_mhz": 18,
            "modes": ["master", "slave"],
            "features": ["dma_support", "crc"]
        },
        "I2C": {
            "available": true,
            "instances": 2,
            "ports": [1, 2],
            "max_speed_khz": 400,
            "features": ["dma_support", "smbus"]
        },
        "ADC": {
            "available": true,
            "instances": 2,
            "channels": 10,
            "resolution_bits": 12,
            "sample_rate_ksps": 1000,
            "features": ["dual_mode", "dma_support", "injected_channels"]
        },
        "Timer": {
            "available": true,
            "instances": 7,
            "timers": [1, 2, 3, 4, 6, 7, 15],
            "advanced_timers": [1],
            "features": ["pwm", "input_capture", "encoder_mode", "dma"]
        },
        "PWM": {
            "available": true,
            "channels": 15,
            "features": ["complementary_output", "dead_time", "break_input"]
        },
        "USB": {
            "available": true,
            "type": "device",
            "speed": "full_speed"
        },
        "CAN": {
            "available": true,
            "instances": 1,
            "features": ["2.0B", "filter_banks"]
        },
        "DMA": {
            "available": true,
            "channels": 7
        }
    },

    "pin_remapping": {
        "available": true,
        "system": "AFIO_REMAP",
        "partial_remap": true
    },

    "interrupts": {
        "total_vectors": 60,
        "priority_levels": 16,
        "nesting": true,
        "system": "NVIC"
    }
}
```

### 10.4. Implementacion hard UART para STM32F103

**`_hard/ST/STM32F1/STM32F103C8/UART/UART.emic`**:
```
EMIC:ifndef _STM32_UART.{port}._EMIC
EMIC:define(_STM32_UART.{port}._EMIC,true)

EMIC:setInput(DEV:_hal/GPIO/gpio.emic)

EMIC:copy(inc/UART.h > TARGET:inc/UART.{port}..h,
          port=.{port}.,BufferSize=.{BufferSize}.,baud=.{baud}.,driver=.{driver}.)

EMIC:copy(src/UART.c > TARGET:UART.{port}..c,
          port=.{port}.,BufferSize=.{BufferSize}.,baud=.{baud}.,driver=.{driver}.)

EMIC:define(c_modules.UART.{port}.,UART.{port}.)

EMIC:endif
```

**`_hard/ST/STM32F1/STM32F103C8/UART/src/UART.c`** (fragmento):
```c
#include "inc/UART.{port}..h"
#include "stm32f1xx.h"

// Seleccion de periferico segun puerto
#if .{port}. == 1
    #define UARTx           USART1
    #define UARTx_IRQn      USART1_IRQn
    #define UARTx_IRQHandler USART1_IRQHandler
    #define UARTx_CLK_EN()  RCC->APB2ENR |= RCC_APB2ENR_USART1EN
    #define UARTx_GPIO_PORT GPIOA
    #define UARTx_TX_PIN    GPIO_PIN_9
    #define UARTx_RX_PIN    GPIO_PIN_10
#elif .{port}. == 2
    #define UARTx           USART2
    #define UARTx_IRQn      USART2_IRQn
    #define UARTx_IRQHandler USART2_IRQHandler
    #define UARTx_CLK_EN()  RCC->APB1ENR |= RCC_APB1ENR_USART2EN
    #define UARTx_GPIO_PORT GPIOA
    #define UARTx_TX_PIN    GPIO_PIN_2
    #define UARTx_RX_PIN    GPIO_PIN_3
#endif

static uint8_t rxBuffer_.{port}.[.{BufferSize}.];
static volatile uint16_t rxHead_.{port}. = 0;
static volatile uint16_t rxTail_.{port}. = 0;

void UART.{port}._init(void) {
    UARTx_CLK_EN();
    // Configurar GPIO para TX (AF Push-Pull) y RX (Input Floating)
    // ...
    UARTx->BRR = SystemCoreClock / .{baud}.;
    UARTx->CR1 = USART_CR1_UE | USART_CR1_TE | USART_CR1_RE | USART_CR1_RXNEIE;
    NVIC_EnableIRQ(UARTx_IRQn);
}

void UART.{port}._bd(uint32_t baudRate) {
    UARTx->BRR = SystemCoreClock / baudRate;
}

void UART.{port}._sendByte(uint8_t data) {
    while (!(UARTx->SR & USART_SR_TXE));
    UARTx->DR = data;
}

uint8_t UART.{port}._readByte(void) {
    if (rxHead_.{port}. == rxTail_.{port}.) return 0;
    uint8_t data = rxBuffer_.{port}.[rxTail_.{port}.];
    rxTail_.{port}. = (rxTail_.{port}. + 1) % .{BufferSize}.;
    return data;
}

uint8_t UART.{port}._dataAvailable(void) {
    return (rxHead_.{port}. != rxTail_.{port}.) ? 1 : 0;
}

void UARTx_IRQHandler(void) {
    if (UARTx->SR & USART_SR_RXNE) {
        rxBuffer_.{port}.[rxHead_.{port}.] = UARTx->DR;
        rxHead_.{port}. = (rxHead_.{port}. + 1) % .{BufferSize}.;
    }
}
```

**Punto clave**: La implementacion STM32 expone exactamente las mismas
funciones que la implementacion PIC24 (`UART{port}_init`, `UART{port}_bd`,
`UART{port}_sendByte`, `UART{port}_readByte`, `UART{port}_dataAvailable`).
Las APIs y drivers que consumen UART via HAL funcionan sin cambios en
ambos MCUs.

---

## 11. Impacto en Capas Superiores

### 11.1. APIs y Drivers

Las APIs y drivers invocan HAL con la misma interfaz:
```
EMIC:setInput(DEV:_hal/UART/UART.emic, port=1, baud=9600, ...)
```

No necesitan saber la ruta interna (`_hard/Microchip/PIC24F/pic24FJ64GA002/`).
El HAL resuelve la ruta usando las macros `system.ucVendor/ucFamily/ucName`.

### 11.2. PCBs

Los PCBs definen tres macros obligatorias:
```
EMIC:define(system.ucVendor, Microchip)
EMIC:define(system.ucFamily, PIC24F)
EMIC:define(system.ucName, pic24FJ128GC006)
```

### 11.3. DevAgent (beneficios nuevos)

El DevAgent puede ahora:

- **Listar MCUs disponibles**: Escanear `_hard/` y leer los `mcu.emic`
  para construir un catalogo de MCUs con sus capacidades
- **Validar compatibilidad**: Antes de generar una API, verificar que
  el MCU del proyecto soporta los perifericos necesarios
- **Sugerir MCU**: Dado un conjunto de requerimientos (2 UARTs, ADC 12-bit,
  USB), filtrar MCUs compatibles
- **Generar documentacion**: Producir fichas tecnicas de MCU a partir del JSON
- **Verificar conflictos de pines**: Usando el pin_map, detectar si dos
  perifericos intentan usar el mismo pin
- **Asistir en diseño de PCB**: Sugerir asignacion de pines basada en
  capacidades disponibles

### 11.4. Procesos EMIC (integracion)

Los nuevos tipos de `EMIC:json` son procesados por distintos procesos:

| Tipo JSON | HardwareInfo | Discovery | Validation | Generate | Visible en sidebar |
|-----------|:---:|:---:|:---:|:---:|:--:|
| `type = peripheral` | Si | — | Si | — | No |
| `type = mcu` | Si | — | Si | — | No |
| `type = mcu_detail` | Si | — | — | — | No |
| `type = pin_map` | Si | — | Si | — | No |
| `type = validation_rules` | — | — | Si | — | No |
| `type = Configurator` | — | Si | — | Si | Si |
| `type = middleware` | — | Si | — | Si | Si |

### 11.5. Documentacion automatica

A partir de los descriptores JSON, el sistema puede generar automaticamente:

**Ficha del MCU**:
> **pic24FJ128GC006** — Microchip PIC24F
> - 16-bit, 128KB Flash, 8KB RAM
> - 32MHz, PLL, XC16 compiler
> - UART ×4, SPI ×3, I2C ×2, ADC 16-bit ×12ch, Timer ×5
> - USB Device, Change Notification, Flash self-write
> - 53 GPIO pins, PPS remapping

**Matriz de compatibilidad API-MCU**:
> | API | Requiere | pic24FJ64GA002 | pic24FJ128GC006 | STM32F103C8 |
> |-----|----------|:-:|:-:|:-:|
> | Temperature | ADC | OK | OK | OK |
> | EMICBus | I2C | OK | OK | OK |
> | USB_API | USB | — | OK | OK |
> | MotorControl | PWM | — | — | OK (via Timer) |

---

## 12. Plan de Implementacion

> **Contexto**: Este plan asume un SDK nuevo desde cero. No hay migracion
> ni compatibilidad con el SDK existente.

### Fase 1 — Infraestructura base

1. Crear la estructura de directorios del nuevo SDK
2. Implementar soporte para `EMIC:json` de los nuevos tipos (`peripheral`,
   `mcu`, `mcu_detail`, `pin_map`, `validation_rules`)
3. Implementar el mecanismo de macros de sistema `system.process.*` en el
   motor de parseo
4. Definir contratos `EMIC:json(type = peripheral)` para los perifericos
   core: GPIO, UART, SPI, I2C, ADC, Timer, PWM, System

**Resultado**: Estructura del SDK lista, contratos definidos, motor de
procesos funcional.

### Fase 2 — Primer MCU completo (referencia)

1. Elegir un MCU de referencia (ej: PIC24FJ128GC006 o STM32F103C8)
2. Crear `_hard/{vendor}/{family}/{model}/` con `mcu.emic` y `pin_map.emic`
3. Implementar todos los perifericos del MCU cumpliendo los contratos HAL
4. Implementar el proceso HardwareInfo para extraer metadata
5. Verificar que Generate produce codigo compilable

**Resultado**: Un MCU completamente funcional con metadata estructurada.

### Fase 3 — Segundo MCU de familia diferente

1. Elegir un MCU de otra familia/vendor (ej: STM32F103C8 si Fase 2 fue PIC,
   o viceversa)
2. Implementar `_hard/{vendor}/{family}/{model}/` con mismos contratos
3. Verificar que las APIs funcionan sin cambios sobre ambos MCUs
4. Validar que HardwareInfo produce catalogo correcto para ambos

**Resultado**: Prueba de concepto multi-familia funcionando con contratos
verificados.

### Fase 4 — Procesos de validacion y documentacion

1. Implementar proceso Validation con cruce contrato↔MCU
2. Implementar proceso PinInfo con gestion de recursos de pines
3. Implementar generacion automatica de documentacion desde metadata
4. Integrar con DevAgent para seleccion de MCU, validacion y sugerencias

**Resultado**: Sistema completo con validacion pre-compilacion,
auto-documentacion y asistencia AI.
