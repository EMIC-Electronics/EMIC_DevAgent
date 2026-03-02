# Diseno de la Capa `_hard/`: Implementaciones MCU-Especificas

> Documento de diseno detallado para la capa `_hard/` del nuevo SDK EMIC.
> Define la estructura interna, convenciones de archivos, patrones de
> implementacion y guias para agregar soporte a nuevos microcontroladores.
>
> **Documento complementario de**: `EMIC_HAL_Hard_Redesign_Proposal.md`
> (que define la arquitectura general HAL+Hard y los Mandatos M1-M4).

---

## Mandatos Aplicables (referencia)

Este documento se rige por los cuatro mandatos definidos en
`EMIC_HAL_Hard_Redesign_Proposal.md`. Se resumen aqui como referencia
rapida — la version completa y normativa esta en el documento padre.

| Mandato | Aplicacion en `_hard/` |
|---------|----------------------|
| **M1** (C99 Freestanding + toolchain nativo) | `_hard/` es la UNICA capa que usa el toolchain nativo. Las funciones expuestas tienen signatures C99 puras. Dentro de la implementacion se permite cualquier extension vendor. |
| **M2** (Escalabilidad) | Agregar un MCU = crear carpeta + archivos. Ningun archivo existente se modifica. |
| **M3** (AI-first) | Cada MCU tiene `mcu.emic` auto-descriptivo con `EMIC:json`. Las rutas son predecibles por convencion. |
| **M4** (Separacion por capas) | `_hard/` NUNCA importa de `_api/`, `_drivers/` ni `_middleware/`. Solo accede a headers del vendor y a `_system/`. |

---

## Indice

0. [Mandatos Aplicables](#mandatos-aplicables-referencia)
1. [Rol de la Capa `_hard/`](#1-rol-de-la-capa-_hard)
2. [Estructura de Directorios](#2-estructura-de-directorios)
3. [Estado Actual (referencia)](#3-estado-actual-referencia)
4. [Anatomia de un MCU en el Nuevo SDK](#4-anatomia-de-un-mcu-en-el-nuevo-sdk)
5. [Descriptor de MCU (`mcu.emic`)](#5-descriptor-de-mcu-mcuemic)
6. [Descriptor de Pines (`pin_map.emic`)](#6-descriptor-de-pines-pin_mapemic)
7. [Patrones de Implementacion de Perifericos](#7-patrones-de-implementacion-de-perifericos)
8. [Patron ISR por Familia](#8-patron-isr-por-familia)
9. [Patron de Buffer Circular (FIFO)](#9-patron-de-buffer-circular-fifo)
10. [Registros de Compilacion (Macros EMIC)](#10-registros-de-compilacion-macros-emic)
11. [Descriptor de Familia](#11-descriptor-de-familia)
12. [Procesos EMIC en `_hard/`](#12-procesos-emic-en-_hard)
13. [Generacion de Pin Headers](#13-generacion-de-pin-headers)
14. [Guia: Agregar un MCU Nuevo](#14-guia-agregar-un-mcu-nuevo)
15. [Guia: Agregar un Periferico a un MCU Existente](#15-guia-agregar-un-periferico-a-un-mcu-existente)
16. [Convenciones de Codigo](#16-convenciones-de-codigo)
17. [Validacion y Verificacion](#17-validacion-y-verificacion)
18. [Impacto en Capas Superiores](#18-impacto-en-capas-superiores)

---

## 1. Rol de la Capa `_hard/`

### Definicion

La capa `_hard/` es la **unica capa del SDK EMIC que contiene codigo
C especifico de un microcontrolador**. Es donde viven los SFRs (Special
Function Registers), las ISRs (Interrupt Service Routines), los accesos
a registros del vendor y cualquier extension propietaria del toolchain.

### Responsabilidades

```
┌──────────────────────────────────────────────────────────────────┐
│  _hard/ — Implementaciones MCU-Especificas                       │
│                                                                  │
│  1. Implementar el contrato HAL para cada periferico             │
│     → Funciones con signatures C99 puras                         │
│     → Logica interna con SFRs/ISRs del vendor                   │
│                                                                  │
│  2. Describir las capacidades del MCU                            │
│     → mcu.emic con EMIC:json(type = mcu)                        │
│     → pin_map.emic con EMIC:json(type = pin_map)                │
│                                                                  │
│  3. Encapsular todo lo vendor-especifico                         │
│     → Headers vendor (#include <xc.h>, stm32f1xx.h, etc.)       │
│     → Extensiones de lenguaje (__attribute__, __near, PROGMEM)   │
│     → Macros de ISR (__interrupt, ISR(), etc.)                   │
│     → Pragmas de configuracion                                   │
│                                                                  │
│  4. Registrar archivos para compilacion                          │
│     → EMIC:define(c_modules.XXX, XXX)                           │
│     → EMIC:define(main_includes.XXX, inc/XXX.h)                  │
└──────────────────────────────────────────────────────────────────┘
```

### Que NO hace `_hard/`

- **NO registra inits ni polls propios**: Los inits/polls se registran
  en la capa `_api/` que los llama en cadena (ver MEMORY: "Regla de
  encadenamiento de inits/polls")
- **NO importa de `_api/`, `_drivers/` ni `_middleware/`**: Solo accede
  a headers vendor y a `_system/` (utilidades core)
- **NO publica recursos al integrador**: Discovery no indexa `_hard/`;
  los recursos se publican en `_api/` via EMIC-Codify
- **NO contiene logica de alto nivel**: Solo inicializacion de hardware,
  transferencia de datos y manejo de interrupciones

### Frontera de portabilidad

La frontera entre `_hard/` y el resto del SDK es el **contrato HAL**:
las function signatures definidas en `_hal/{PERIFERICO}/{periferico}.emic`.

```
    Capas superiores           │          _hard/
    (C99 Freestanding)         │    (C + toolchain nativo)
                               │
    void UART1_sendByte(       │    void UART1_sendByte(
        uint8_t data);    ←────┼────    uint8_t data) {
                               │        while (!(U1STA & 0x0200));
    // Solo ve la signature    │        U1TXREG = data;
    // No sabe como funciona   │    }
                               │
                               │    // Usa SFRs del vendor
                               │    // Puede usar __attribute__,
                               │    // pragmas, asm inline, etc.
```

---

## 2. Estructura de Directorios

### Jerarquia: `vendor / family / model`

```
_hard/
├── Microchip/
│   ├── PIC24F/                              ← Familia
│   │   ├── PIC24F.family.emic               ← Descriptor de familia
│   │   ├── _shared/                         ← Codigo compartido entre modelos
│   │   │   ├── FIFO/                        ← Buffer circular reutilizable
│   │   │   │   ├── inc/fifo.h
│   │   │   │   └── src/fifo.c
│   │   │   └── PPS/                         ← Pin remapping compartido
│   │   │       └── inc/pps.h
│   │   ├── pic24FJ64GA002/                  ← Modelo especifico
│   │   │   ├── mcu.emic                     ← Descriptor del MCU
│   │   │   ├── pins/
│   │   │   │   └── pin_map.emic             ← Mapa de pines
│   │   │   ├── System/
│   │   │   │   ├── system.emic
│   │   │   │   ├── inc/system.h
│   │   │   │   └── src/system.c
│   │   │   ├── GPIO/
│   │   │   │   └── gpio.emic
│   │   │   ├── UART/
│   │   │   │   ├── UART.emic                ← Orquestador parametrizado
│   │   │   │   ├── inc/UART.h
│   │   │   │   └── src/UART.c
│   │   │   ├── SPI/
│   │   │   ├── I2C/
│   │   │   ├── ADC/
│   │   │   └── Timer/
│   │   ├── pic24FJ128GA010/
│   │   │   └── ...
│   │   └── pic24FJ128GC006/
│   │       └── ...
│   ├── dsPIC33/
│   │   ├── dsPIC33.family.emic
│   │   ├── _shared/
│   │   └── dsPIC33EP512MC806/
│   │       └── ...
│   └── PIC32MZ/
│       ├── PIC32MZ.family.emic
│       └── PIC32MZ2048EFM064/
│           └── ...
├── ST/
│   ├── STM32F1/
│   │   ├── STM32F1.family.emic
│   │   ├── _shared/
│   │   └── STM32F103C8/
│   │       ├── mcu.emic
│   │       ├── pins/pin_map.emic
│   │       ├── System/
│   │       ├── GPIO/
│   │       ├── UART/
│   │       ├── SPI/
│   │       ├── I2C/
│   │       ├── ADC/
│   │       └── Timer/
│   └── STM32F4/
│       └── STM32F407VG/
├── Espressif/
│   └── ESP32/
│       └── ESP32-WROOM-32/
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

### Reglas de naming (M3)

| Elemento | Convencion | Ejemplo |
|----------|-----------|---------|
| Vendor | PascalCase, nombre oficial | `Microchip`, `ST`, `Espressif` |
| Family | Nombre oficial del vendor | `PIC24F`, `STM32F1`, `ESP32` |
| Model | Nombre exacto del datasheet | `pic24FJ128GC006`, `STM32F103C8` |
| Periferico (carpeta) | UPPER_CASE, coincide con contrato HAL | `UART/`, `SPI/`, `GPIO/` |
| Archivo .emic | lower_case, coincide con contrato HAL | `UART.emic`, `adc.emic` |
| Archivo .c/.h | lower_case o UPPER_CASE segun periferico | `UART.c`, `adc.c` |
| Descriptor MCU | Siempre `mcu.emic` | `_hard/ST/STM32F1/STM32F103C8/mcu.emic` |
| Pin map | Siempre `pin_map.emic` | `_hard/.../pins/pin_map.emic` |
| Descriptor familia | `{Family}.family.emic` | `PIC24F.family.emic` |

### Ruta predecible (M3)

Un agente AI puede construir cualquier ruta sin buscar en el filesystem:

```
Descriptor MCU:     _hard/{vendor}/{family}/{model}/mcu.emic
Pin map:            _hard/{vendor}/{family}/{model}/pins/pin_map.emic
Periferico:         _hard/{vendor}/{family}/{model}/{PERIFERICO}/
Orquestador:        _hard/{vendor}/{family}/{model}/{PERIFERICO}/{periferico}.emic
Header:             _hard/{vendor}/{family}/{model}/{PERIFERICO}/inc/{periferico}.h
Source:             _hard/{vendor}/{family}/{model}/{PERIFERICO}/src/{periferico}.c
Familia:            _hard/{vendor}/{family}/{Family}.family.emic
Compartido:         _hard/{vendor}/{family}/_shared/
```

---

## 3. Estado Actual (referencia)

> **Nota**: Esta seccion documenta el SDK existente como referencia para
> entender las decisiones de diseno del nuevo SDK. El nuevo SDK se crea
> desde cero.

### Estructura actual de `_hard/`

```
_hard/
├── pic24FJ64GA002/          ← Plano (sin vendor/family)
├── pic24FJ128GA010/
├── pic24FJ128GC006/
├── dsPIC33EP512MC806/
└── PIC32MZ2048EFM064/
```

Solo 5 MCUs, todos Microchip PIC. No hay jerarquia vendor/family.

### Perifericos implementados

| MCU | GPIO | ADC | UART | SPI | I2C | Timer | PWM | Flash | RefClk | CN |
|-----|:----:|:---:|:----:|:---:|:---:|:-----:|:---:|:-----:|:------:|:--:|
| pic24FJ64GA002 | Si | Si | Si | Si | Si | Si | — | — | — | — |
| pic24FJ128GA010 | Si | Si | Si | Si | Si | Si | — | — | — | — |
| pic24FJ128GC006 | Si | Si | Si | Si | Si | Si | — | Si | — | Si |
| dsPIC33EP512MC806 | Si | — | Si | Si | — | Si | Si | — | Si | — |
| PIC32MZ2048EFM064 | Si | — | — | — | — | — | — | — | — | — |

### Patrones de archivos actuales

**Tipo A — Parametrizado** (UART, SPI, I2C):
- Usa `EMIC:setOutput/restoreOutput` para generar multiples instancias
- Placeholder `.{port}.` en nombres de funciones y archivos
- Ejemplo: `UART.emic` genera `UART1.c`, `UART2.c`, etc.

**Tipo B — No parametrizado** (ADC, Timer, System):
- Usa `EMIC:copy` simple
- Una sola instancia por MCU
- Ejemplo: `adc.emic` genera `adc.c`

**Tipo C — Passthrough** (GPIO):
- Delega a archivos de pin individuales
- No genera codigo propio, solo configura macros

### Patron ISR actual (PIC24/dsPIC)

```c
void __attribute__((interrupt(auto_psv))) _U1RXInterrupt(void) {
    IFS0bits.U1RXIF = 0;  // Clear interrupt flag
    uint8_t data = U1RXREG;
    // ... almacenar en FIFO
}
```

### Patron de registro PPS actual (PIC24)

```c
__builtin_write_OSCCONL(OSCCON & ~(1 << 6));  // Unlock PPS
RPOR_TX_.{driver}. = 3;                        // Assign TX to pin
RPINR18bits.U1RXR = RPIN_.{driver}.;           // Assign RX to pin
__builtin_write_OSCCONL(OSCCON | (1 << 6));    // Lock PPS
```

### Patron de pin header actual (`setPinB12.h`)

```c
#define TRIS_.{name}.       _TRISB12
#define PORT_.{name}.       _RB12
#define LAT_.{name}.        _LATB12
#define ODC_.{name}.        _ODB12
#define PIN_.{name}.        _RB12
#define CN_.{name}.         14
#define ADC_value_.{name}.  Buffer_entradas[12]
#define HAL_SetAnalog_.{name}.()  {_ANSB12=1; adc_addAnalogChannel(12);}
```

### Limitaciones del diseno actual

1. **Monocultura**: Solo Microchip PIC — registros similares, mismo toolchain
2. **Sin descriptor MCU**: Las capacidades no estan codificadas — solo se
   descubren intentando compilar
3. **Sin contrato formal**: Los nombres de funciones son iguales por
   convencion manual, no por verificacion
4. **Pin headers individuales**: Un archivo `.h` por pin, sin metadata
   estructurada sobre capacidades
5. **Dos patrones de orquestacion**: `setOutput/restoreOutput` (PIC24) vs
   `copy` (dsPIC) — inconsistencia interna
6. **Sin procesos EMIC**: Los archivos solo se usan durante Generate

---

## 4. Anatomia de un MCU en el Nuevo SDK

### Archivos obligatorios

Todo MCU en el nuevo SDK DEBE tener estos archivos:

```
_hard/{vendor}/{family}/{model}/
├── mcu.emic                     ← OBLIGATORIO: Descriptor del MCU
├── pins/
│   └── pin_map.emic             ← OBLIGATORIO: Mapa de pines
└── System/
    ├── system.emic              ← OBLIGATORIO: Configuracion de sistema
    ├── inc/system.h             ← Prototipos + config pragmas
    └── src/system.c             ← Clock init, startup
```

### Archivos por periferico soportado

Por cada periferico que el MCU soporta, se crea una carpeta:

```
_hard/{vendor}/{family}/{model}/{PERIFERICO}/
├── {periferico}.emic            ← Orquestador EMIC
├── inc/{periferico}.h           ← Prototipos + tipos + registros
└── src/{periferico}.c           ← Implementacion con SFRs/ISRs
```

### Archivo de familia (compartido)

Cada familia tiene un descriptor a nivel de directorio de familia:

```
_hard/{vendor}/{family}/
├── {Family}.family.emic         ← Descriptor de familia
└── _shared/                     ← Codigo compartido entre modelos
    ├── FIFO/                    ← Buffer circular
    └── ...                      ← Otros componentes compartidos
```

### Ejemplo completo: STM32F103C8

```
_hard/ST/STM32F1/STM32F103C8/
├── mcu.emic                     ← Descriptor: ARM Cortex-M3, 72MHz...
├── pins/
│   └── pin_map.emic             ← 37 GPIO pins, LQFP-48
├── System/
│   ├── system.emic              ← Orquestador: clock, startup, linker
│   ├── inc/system.h             ← #include "stm32f1xx.h", prototipos
│   └── src/system.c             ← SystemInit(), clock tree config
├── GPIO/
│   ├── gpio.emic
│   ├── inc/gpio.h
│   └── src/gpio.c
├── UART/
│   ├── UART.emic                ← Parametrizado: port, baud, BufferSize
│   ├── inc/UART.h
│   └── src/UART.c
├── SPI/
│   ├── SPI.emic                 ← Parametrizado: port, mode, speed
│   ├── inc/SPI.h
│   └── src/SPI.c
├── I2C/
│   ├── I2C.emic
│   ├── inc/I2C.h
│   └── src/I2C.c
├── ADC/
│   ├── adc.emic                 ← No parametrizado: instancia unica
│   ├── inc/adc.h
│   └── src/adc.c
├── Timer/
│   ├── Timer.emic               ← Parametrizado: timer_number
│   ├── inc/Timer.h
│   └── src/Timer.c
└── PWM/
    ├── PWM.emic                 ← Parametrizado: channel
    ├── inc/PWM.h
    └── src/PWM.c
```

---

## 5. Descriptor de MCU (`mcu.emic`)

### Proposito

El `mcu.emic` es el **archivo de identidad del microcontrolador**. Es
lo primero que un agente AI lee para entender que puede hacer un MCU.
Contiene:
- Identificacion (vendor, family, model)
- Capacidades de hardware (perifericos, memoria, clock)
- Informacion de toolchain
- Secciones condicionales por proceso EMIC

### Estructura completa

```
// @layer: hard
// @type: mcu_descriptor
// @model: {model}

// ================================================================
// SECCION 1: Siempre visible (metadata comun)
// ================================================================
EMIC:json(type = mcu)
{
    "schema_version": "1.0",
    "vendor": "{Vendor}",
    "family": "{Family}",
    "model": "{model}",
    "brief": "Descripcion corta: arquitectura, freq, Flash, RAM",
    "architecture": "ARM | MIPS16 | AVR | RISC-V | Xtensa",
    "core": "Cortex-M3 | PIC24 | ATmega | RV32IMAC | LX6",
    "bits": 8 | 16 | 32,

    "toolchain": {
        "compiler": "arm-none-eabi-gcc | XC16 | XC8 | avr-gcc | ...",
        "compiler_base": "gcc | gcc-fork | proprietary | open-source",
        "c_standard": "C99",
        "ide": "STM32CubeIDE | MPLAB X | Arduino IDE | ...",
        "header": "stm32f1xx.h | <xc.h> | <avr/io.h> | ...",
        "programmer": "ST-Link V2 | PICkit3 | AVRISP | ...",
        "linker_script": "STM32F103C8Tx_FLASH.ld | p24FJ128GC006.gld | ..."
    },

    "memory": {
        "flash_kb": 64,
        "ram_kb": 20,
        "eeprom_bytes": 0,
        "flash_page_size": 1024,
        "flash_row_size": null
    },

    "clock": {
        "max_frequency_mhz": 72,
        "internal_oscillator_mhz": 8,
        "pll_available": true,
        "fcy_formula": "HCLK | FOSC/2 | F_CPU"
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
        // ... todos los perifericos ...
    },

    "pin_remapping": {
        "available": true,
        "system": "PPS | AFIO_REMAP | GPIO_AF | none",
        "partial_remap": false
    },

    "interrupts": {
        "total_vectors": 60,
        "priority_levels": 16,
        "nesting": true,
        "system": "NVIC | IVT | INT0-INT7"
    }
}

// ================================================================
// SECCION 2: HardwareInfo — detalles extendidos
// ================================================================
EMIC:ifdef system.process.HardwareInfo

EMIC:json(type = mcu_detail)
{
    "schema_version": "1.0",
    "memory_map": {
        "flash_start": "0x08000000",
        "ram_start": "0x20000000",
        "peripheral_start": "0x40000000"
    },
    "clock_tree": { ... },
    "dma_channels": [ ... ],
    "power_modes": [ ... ]
}

EMIC:endif

// ================================================================
// SECCION 3: Validation — reglas de validacion
// ================================================================
EMIC:ifdef system.process.Validation

EMIC:json(type = validation_rules)
{
    "schema_version": "1.0",
    "constraints": [
        {
            "rule": "max_uart_baud",
            "value": 4500000,
            "message": "Max UART baud rate exceeded"
        },
        {
            "rule": "adc_max_channels_simultaneous",
            "value": 2,
            "message": "Only 2 ADC units available for dual mode"
        }
    ]
}

EMIC:endif

// ================================================================
// SECCION 4: Generate — macros de compilacion
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

### Campos obligatorios del JSON `type = mcu`

| Campo | Tipo | Descripcion |
|-------|------|-------------|
| `schema_version` | string | Siempre `"1.0"` (M3: forward compat) |
| `vendor` | string | Nombre del fabricante |
| `family` | string | Familia del MCU |
| `model` | string | Modelo exacto |
| `brief` | string | Descripcion corta legible |
| `architecture` | string | Arquitectura del procesador |
| `core` | string | Core especifico |
| `bits` | number | Ancho de datos nativo (8, 16, 32) |
| `toolchain` | object | Compilador, IDE, header, programmer |
| `toolchain.compiler` | string | Nombre del compilador |
| `toolchain.compiler_base` | string | Base: `gcc`, `gcc-fork`, `proprietary` |
| `toolchain.c_standard` | string | Siempre `"C99"` |
| `memory` | object | Flash, RAM, EEPROM |
| `clock` | object | Frecuencia, PLL, formula FCY |
| `peripherals` | object | Mapa de perifericos disponibles |

### Regla de consistencia

Los keys del objeto `peripherals` DEBEN coincidir exactamente con los
nombres de los contratos HAL en `_hal/{PERIFERICO}/`. Ejemplo:
- `peripherals.UART` → contrato en `_hal/UART/UART.emic`
- `peripherals.ADC` → contrato en `_hal/ADC/adc.emic`
- `peripherals.GPIO` → contrato en `_hal/GPIO/gpio.emic`

Si un periferico existe en el MCU pero no tiene contrato HAL, se lista
con `"available": true` pero se agrega `"no_contract": true` para
indicar que no es consumible por las capas superiores (todavia).

---

## 6. Descriptor de Pines (`pin_map.emic`)

### Proposito

El `pin_map.emic` reemplaza la coleccion de archivos `setPinXX.h`
individuales con un descriptor JSON estructurado. Es la **fuente unica
de verdad** sobre los pines del MCU. A partir de el, el sistema genera
automaticamente los headers de pin durante Generate.

### Estructura

La estructura completa de `EMIC:json(type = pin_map)` esta definida
en la Seccion 9 de `EMIC_HAL_Hard_Redesign_Proposal.md`. Aqui se
documentan las reglas especificas de implementacion.

### Reglas de llenado

1. **Todo pin de I/O del MCU debe estar listado** — incluso los que
   tienen funciones fijas (I2C, USB, oscilador)
2. **`capabilities`** es un array de strings con valores de un
   vocabulario controlado:

   | Capability | Significado |
   |-----------|-------------|
   | `digital_io` | GPIO digital (input/output) |
   | `analog_input` | Entrada ADC |
   | `analog_output` | Salida DAC |
   | `change_notification` | Interrupt on change |
   | `remappable_io` | Pin remappable (PPS, AFIO, GPIO_AF) |
   | `pwm_output` | Salida PWM (fija, no remappable) |
   | `open_drain` | Soporta modo open-drain |
   | `5v_tolerant` | Tolerante a 5V en input |

3. **`registers`** contiene los nombres exactos de los SFRs del vendor.
   Estos valores se usan textualmente en el codigo C generado.

4. **`fixed_functions`** lista las funciones de periferico que estan
   cableadas a pines fijos (no remappable):
   ```json
   {"function": "SDA1", "pin": "RG3", "peripheral": "I2C", "port": 1}
   ```

5. **Pines especiales** (VDD, VSS, MCLR, OSC) se listan en
   `special_pins` con su tipo (`power`, `ground`, `reset`, `oscillator`).

### Generacion de pin headers

Durante el proceso Generate, el sistema lee `pin_map.emic` y genera
un archivo `.h` por cada pin asignado en el PCB. El formato de salida
es:

```c
// Auto-generated from pin_map.emic
// Pin: {pin.id} (physical pin {pin.pin_number})

#define TRIS_.{name}.       {pin.registers.tris}
#define PORT_.{name}.       {pin.registers.port}
#define LAT_.{name}.        {pin.registers.lat}
#define ODC_.{name}.        {pin.registers.odc}
#define PIN_.{name}.        {pin.registers.port}
```

Macros adicionales se generan condicionalmente segun capabilities:

```c
// Solo si "remappable_io" in capabilities:
#define RPOUT_.{name}.      {pin.rpout_register}
#define RPIN_.{name}.       {pin.rpin_number}

// Solo si "analog_input" in capabilities:
#define ADC_value_.{name}.  Buffer_entradas[{pin.adc_buffer_index}]
#define HAL_SetAnalog_.{name}.()  {{pin.registers.ansel}=1; adc_addAnalogChannel({pin.analog_channel});}

// Solo si "change_notification" in capabilities:
#define CN_.{name}.         {pin.cn_number}
```

---

## 7. Patrones de Implementacion de Perifericos

### 7.1. Patron A — Periferico Parametrizado (multi-instancia)

Usado por perifericos con multiples instancias: **UART, SPI, I2C, Timer**.

El placeholder `.{port}.` (o `.{timer_number}.`, `.{channel}.`) se
sustituye durante `EMIC:copy` para generar codigo unico por instancia.

**Orquestador `UART.emic`:**

```
// @layer: hard
// @peripheral: UART
// @pattern: parameterized
// @parameters: port, baud, BufferSize, driver

EMIC:ifndef _HARD_UART.{port}._EMIC
EMIC:define(_HARD_UART.{port}._EMIC, true)

// Dependencia: GPIO para configuracion de pines
EMIC:setInput(DEV:_hal/GPIO/gpio.emic)

// Generar archivos para esta instancia
EMIC:copy(inc/UART.h > TARGET:inc/UART.{port}..h,
          port=.{port}.,BufferSize=.{BufferSize}.,baud=.{baud}.,driver=.{driver}.)

EMIC:copy(src/UART.c > TARGET:UART.{port}..c,
          port=.{port}.,BufferSize=.{BufferSize}.,baud=.{baud}.,driver=.{driver}.)

// Registrar para compilacion
EMIC:define(c_modules.UART.{port}., UART.{port}.)

EMIC:endif
```

**Variante con `setOutput/restoreOutput`** (PIC24 actual):

```
EMIC:ifndef _HARD_UART.{port}._EMIC
EMIC:define(_HARD_UART.{port}._EMIC, true)

EMIC:setInput(DEV:_hal/GPIO/gpio.emic)

// setOutput redirige la salida a un archivo especifico
EMIC:setOutput(TARGET:inc/UART.{port}..h)
EMIC:copy(inc/UART.h, port=.{port}.,BufferSize=.{BufferSize}.,baud=.{baud}.,driver=.{driver}.)
EMIC:restoreOutput

EMIC:setOutput(TARGET:UART.{port}..c)
EMIC:copy(src/UART.c, port=.{port}.,BufferSize=.{BufferSize}.,baud=.{baud}.,driver=.{driver}.)
EMIC:restoreOutput

EMIC:define(c_modules.UART.{port}., UART.{port}.)

EMIC:endif
```

**Recomendacion para el nuevo SDK**: Usar `EMIC:copy(src > dest)` como
patron unico. Es mas claro, mas conciso y equivalente funcionalmente.

### 7.2. Patron B — Periferico No Parametrizado (instancia unica)

Usado por perifericos singleton: **ADC, System, Flash**.

No hay placeholder de instancia. El archivo se copia una sola vez.

**Orquestador `adc.emic`:**

```
// @layer: hard
// @peripheral: ADC
// @pattern: singleton

EMIC:ifndef _HARD_ADC_EMIC
EMIC:define(_HARD_ADC_EMIC, true)

EMIC:copy(inc/adc.h > TARGET:inc/adc.h)
EMIC:copy(src/adc.c > TARGET:adc.c)

EMIC:define(c_modules.adc, adc)

EMIC:endif
```

### 7.3. Patron C — GPIO (configuracion de pines)

GPIO es un caso especial: no genera archivos propios de codigo, sino
que configura los pines definidos en el PCB. Los pines se configuran
via los pin headers generados desde `pin_map.emic`.

**Orquestador `gpio.emic`:**

```
// @layer: hard
// @peripheral: GPIO
// @pattern: pin_config

EMIC:ifndef _HARD_GPIO_EMIC
EMIC:define(_HARD_GPIO_EMIC, true)

// Incluir headers de pines asignados por el PCB
EMIC:setInput(DEV:_hard/.{system.ucVendor}./.{system.ucFamily}./.{system.ucName}./pins/pin_headers.emic)

EMIC:endif
```

### 7.4. Patron D — Periferico con Recursos Compartidos

Perifericos que comparten codigo a nivel de familia. Ejemplo: el buffer
FIFO es identico para todos los PIC24F.

**Orquestador que referencia `_shared/`:**

```
// @layer: hard
// @peripheral: UART
// @pattern: parameterized + shared

EMIC:ifndef _HARD_UART.{port}._EMIC
EMIC:define(_HARD_UART.{port}._EMIC, true)

EMIC:setInput(DEV:_hal/GPIO/gpio.emic)

// Componente compartido de familia (FIFO)
EMIC:ifndef _SHARED_FIFO
EMIC:define(_SHARED_FIFO, true)
EMIC:copy(DEV:_hard/.{system.ucVendor}./.{system.ucFamily}./_shared/FIFO/inc/fifo.h > TARGET:inc/fifo.h)
EMIC:copy(DEV:_hard/.{system.ucVendor}./.{system.ucFamily}./_shared/FIFO/src/fifo.c > TARGET:fifo.c)
EMIC:define(c_modules.fifo, fifo)
EMIC:endif

// Archivos especificos del modelo
EMIC:copy(inc/UART.h > TARGET:inc/UART.{port}..h,
          port=.{port}.,BufferSize=.{BufferSize}.,baud=.{baud}.,driver=.{driver}.)
EMIC:copy(src/UART.c > TARGET:UART.{port}..c,
          port=.{port}.,BufferSize=.{BufferSize}.,baud=.{baud}.,driver=.{driver}.)

EMIC:define(c_modules.UART.{port}., UART.{port}.)

EMIC:endif
```

---

## 8. Patron ISR por Familia

Cada familia de MCU tiene su propio mecanismo de interrupciones. La
capa `_hard/` encapsula estas diferencias. Las ISRs son transparentes
para las capas superiores.

### Microchip PIC24 / dsPIC

```c
// Vector de interrupcion nombrado con prefijo _
// auto_psv: preserva el PSV register automaticamente
void __attribute__((interrupt(auto_psv))) _U.{port}.RXInterrupt(void) {
    IFS0bits.U.{port}.RXIF = 0;     // Clear flag obligatorio primero
    uint8_t data = U.{port}.RXREG;  // Leer dato del registro de recepcion
    fifo_push(&rxFifo_.{port}., data);
}
```

**Notas**:
- Vector names: `_U1RXInterrupt`, `_U2RXInterrupt`, `_SPI1Interrupt`, etc.
- El flag de interrupcion se limpia PRIMERO, antes de leer datos
- `auto_psv` es obligatorio para evitar corrupcion de `const` en PSVPAG
- Priority bits disponibles en `IPC{n}bits.U{port}RXIP`

### Microchip PIC32

```c
// ISR via atributos especificos de PIC32
void __attribute__((vector(_UART1_RX_VECTOR), interrupt(IPL3SOFT),
                    nomips16)) UART1_RXHandler(void) {
    IFS1CLR = _IFS1_U1RXIF_MASK;    // Clear flag via CLR register
    uint8_t data = U1RXREG;
    fifo_push(&rxFifo_1, data);
}
```

### ST STM32 (ARM Cortex-M)

```c
// ISR nombrada segun tabla de vectores del startup file
// No usa __attribute__ — el nombre es el vector
void USART.{port}._IRQHandler(void) {
    if (USART.{port}.->SR & USART_SR_RXNE) {
        uint8_t data = USART.{port}.->DR;
        fifo_push(&rxFifo_.{port}., data);
    }
}
```

**Notas**:
- Los nombres son fijos: `USART1_IRQHandler`, `SPI1_IRQHandler`, etc.
- Se habilitan via NVIC: `NVIC_EnableIRQ(USART1_IRQn)`
- No requiere clear explicito del flag; leer DR limpia RXNE automaticamente

### Atmel AVR

```c
// ISR macro de avr-libc
ISR(USART_RX_vect) {
    uint8_t data = UDR0;
    fifo_push(&rxFifo, data);
}
```

**Notas**:
- Usa macro `ISR()` de `<avr/interrupt.h>`
- Nombres de vectores definidos en el header del MCU
- `cli()`/`sei()` para disable/enable global interrupts

### Espressif ESP32 (Xtensa / RISC-V)

```c
// ISR registrada dinamicamente via ESP-IDF
static void IRAM_ATTR uart_isr_handler(void *arg) {
    uint32_t status = UART.{port}..int_st.val;
    if (status & UART_RXFIFO_FULL_INT_ST) {
        while (UART.{port}..status.rxfifo_cnt) {
            uint8_t data = UART.{port}..fifo.rw_byte;
            fifo_push(&rxFifo_.{port}., data);
        }
        UART.{port}..int_clr.rxfifo_full_int_clr = 1;
    }
}

// En init:
esp_intr_alloc(ETS_UART.{port}._INTR_SOURCE, ESP_INTR_FLAG_IRAM,
               uart_isr_handler, NULL, NULL);
```

### Tabla resumen

| Familia | Declaracion ISR | Clear flag | Enable | Global disable |
|---------|----------------|------------|--------|---------------|
| PIC24/dsPIC | `__attribute__((interrupt(auto_psv)))` | `IFSxbits.flag = 0` | `IECxbits.flag = 1` | `__builtin_disi(0x3FFF)` |
| PIC32 | `__attribute__((vector(), interrupt()))` | `IFSxCLR = mask` | `IECxSET = mask` | `__builtin_disable_interrupts()` |
| STM32 (Cortex-M) | `void XXX_IRQHandler(void)` | Auto/manual | `NVIC_EnableIRQ()` | `__disable_irq()` |
| AVR | `ISR(VECTOR_vect)` | Automatico | `UCSR0B \|= (1<<RXCIE0)` | `cli()` |
| ESP32 | `IRAM_ATTR` + `esp_intr_alloc` | Manual | `esp_intr_alloc()` | `portDISABLE_INTERRUPTS()` |

---

## 9. Patron de Buffer Circular (FIFO)

### Proposito

Todo periferico de comunicacion (UART, SPI, I2C) necesita un buffer
circular para almacenar datos recibidos por ISR. El patron FIFO es
identico para todas las familias de MCU — lo que cambia es el
mecanismo de proteccion de concurrencia.

### Implementacion canonica

```c
// inc/fifo.h
#ifndef FIFO_H
#define FIFO_H

#include <stdint.h>
#include <stdbool.h>

typedef struct {
    uint8_t *buffer;
    uint16_t size;
    volatile uint16_t head;   // Escrito por ISR
    volatile uint16_t tail;   // Leido por main loop
    volatile uint16_t count;  // Elementos disponibles
} fifo_t;

void fifo_init(fifo_t *f, uint8_t *buf, uint16_t size);
bool fifo_push(fifo_t *f, uint8_t data);
bool fifo_pop(fifo_t *f, uint8_t *data);
bool fifo_isEmpty(fifo_t *f);
uint16_t fifo_count(fifo_t *f);

#endif
```

```c
// src/fifo.c
#include "inc/fifo.h"

void fifo_init(fifo_t *f, uint8_t *buf, uint16_t size) {
    f->buffer = buf;
    f->size = size;
    f->head = 0;
    f->tail = 0;
    f->count = 0;
}

bool fifo_push(fifo_t *f, uint8_t data) {
    if (f->count >= f->size) return false;  // Buffer lleno
    f->buffer[f->head] = data;
    f->head = (f->head + 1) % f->size;
    f->count++;
    return true;
}

bool fifo_pop(fifo_t *f, uint8_t *data) {
    if (f->count == 0) return false;  // Buffer vacio
    *data = f->buffer[f->tail];
    f->tail = (f->tail + 1) % f->size;
    f->count--;
    return true;
}

bool fifo_isEmpty(fifo_t *f) {
    return (f->count == 0);
}

uint16_t fifo_count(fifo_t *f) {
    return f->count;
}
```

### Proteccion de concurrencia por familia

La unica parte que varia entre familias es como deshabilitar las
interrupciones durante accesos criticos desde el main loop:

```c
// PIC24/dsPIC
#define CRITICAL_ENTER()  __builtin_disi(0x3FFF)
#define CRITICAL_EXIT()   __builtin_disi(0x0000)

// PIC32
#define CRITICAL_ENTER()  unsigned int _st = __builtin_disable_interrupts()
#define CRITICAL_EXIT()   __builtin_mtc0(12, 0, _st)

// STM32 (Cortex-M)
#define CRITICAL_ENTER()  __disable_irq()
#define CRITICAL_EXIT()   __enable_irq()

// AVR
#define CRITICAL_ENTER()  cli()
#define CRITICAL_EXIT()   sei()

// ESP32
#define CRITICAL_ENTER()  portENTER_CRITICAL(&mux)
#define CRITICAL_EXIT()   portEXIT_CRITICAL(&mux)
```

### Ubicacion en el SDK

- **Candidato a `_shared/`**: Si todos los modelos de una familia usan
  el mismo FIFO, se coloca en `_hard/{vendor}/{family}/_shared/FIFO/`
- **Candidato a `_system/`**: Si la implementacion es 100% C99 y no
  necesita macros de concurrencia vendor-especificas, puede vivir en
  `_system/` como utilidad compartida por todo el SDK
- **Hibrido (recomendado)**: `fifo.c`/`fifo.h` en `_system/` con las
  funciones puras, y macros `CRITICAL_ENTER/EXIT` definidas en
  `_hard/{vendor}/{family}/_shared/critical.h`

---

## 10. Registros de Compilacion (Macros EMIC)

### Que registra `_hard/`

Cuando un periferico es incluido via HAL, el orquestador `.emic`
registra macros que informan al sistema de compilacion que archivos
deben ser incluidos.

### Patron `c_modules`

```
EMIC:define(c_modules.UART1, UART1)
EMIC:define(c_modules.adc, adc)
EMIC:define(c_modules.system, system)
```

**Efecto en `main.c`**: El template de main genera las directivas de
compilacion a partir de `c_modules.*`:

```
// main.c template expandido
EMIC:foreach(c_modules.*)
#include ".{*}..c"          // o referencia al linker
EMIC:endforeach
```

### Patron `main_includes`

```
EMIC:define(main_includes.UART1, inc/UART1.h)
EMIC:define(main_includes.adc, inc/adc.h)
```

**Efecto**: Genera las lineas `#include` en el main:

```c
// Generado por main template
#include "inc/UART1.h"
#include "inc/adc.h"
```

### Patron `includes_head` / `includes_src`

Usado para archivos que necesitan estar en posiciones especiales del
orden de compilacion:

```
EMIC:define(includes_head.system, inc/system.h)   // Al principio de todos los includes
EMIC:define(includes_src.fifo, fifo)               // En el grupo de sources del sistema
```

### Reglas criticas (de MEMORY)

> La capa `_hard` solo registra `c_modules.xxx` (para que compile),
> NO registra `inits` ni `polls` propios. La capa `_api` registra
> sus propios `inits` y `polls` via `EMIC:define(inits.X, X_init)` /
> `EMIC:define(polls.X, X_poll)`.

Esto evita duplicacion de llamadas y mantiene el control del orden
de ejecucion en la capa que le corresponde (la API).

---

## 11. Descriptor de Familia

### Proposito

El descriptor de familia (`{Family}.family.emic`) documenta las
caracteristicas comunes a todos los modelos de una familia. Permite
a un agente AI entender que MCUs comparten base de codigo y que
variaciones existen entre modelos.

### Estructura

```
// @layer: hard
// @type: family_descriptor

EMIC:json(type = family)
{
    "schema_version": "1.0",
    "vendor": "Microchip",
    "family": "PIC24F",
    "architecture": "MIPS16",
    "core": "PIC24",
    "bits": 16,
    "brief": "16-bit PIC24F General Purpose microcontrollers",

    "common_toolchain": {
        "compiler": "XC16",
        "compiler_base": "gcc-fork",
        "c_standard": "C99",
        "header": "<xc.h>"
    },

    "shared_code": [
        {
            "component": "FIFO",
            "path": "_shared/FIFO/",
            "brief": "Circular buffer for UART/SPI/I2C reception"
        },
        {
            "component": "PPS",
            "path": "_shared/PPS/",
            "brief": "Peripheral Pin Select unlock/lock sequence"
        }
    ],

    "models": [
        {
            "model": "pic24FJ64GA002",
            "path": "pic24FJ64GA002/",
            "flash_kb": 64,
            "ram_kb": 8,
            "pins_io": 21,
            "package": "DIP-28"
        },
        {
            "model": "pic24FJ128GA010",
            "path": "pic24FJ128GA010/",
            "flash_kb": 128,
            "ram_kb": 8,
            "pins_io": 85,
            "package": "TQFP-100"
        },
        {
            "model": "pic24FJ128GC006",
            "path": "pic24FJ128GC006/",
            "flash_kb": 128,
            "ram_kb": 8,
            "pins_io": 53,
            "package": "TQFP-64"
        }
    ],

    "isr_pattern": {
        "declaration": "__attribute__((interrupt(auto_psv)))",
        "naming": "_{VectorName}",
        "clear_flag": "IFSxbits.flag = 0",
        "enable": "IECxbits.flag = 1",
        "global_disable": "__builtin_disi(0x3FFF)"
    },

    "register_access": {
        "style": "bitfield_struct",
        "example": "U1MODEbits.UARTEN = 1",
        "sfr_header": "<xc.h>"
    },

    "pin_remapping": {
        "system": "PPS",
        "unlock": "__builtin_write_OSCCONL(OSCCON & ~(1 << 6))",
        "lock": "__builtin_write_OSCCONL(OSCCON | (1 << 6))",
        "input_register": "RPINRxbits",
        "output_register": "RPORxbits"
    }
}
```

### Utilidad para agentes AI

Un agente que necesita agregar un nuevo modelo PIC24F puede:

1. Leer `PIC24F.family.emic` para entender los patrones comunes
2. Copiar un modelo existente como base
3. Ajustar solo las diferencias (flash, ram, pines, perifericos)
4. Reutilizar los componentes `_shared/`

---

## 12. Procesos EMIC en `_hard/`

### Que procesos afectan a `_hard/`

| Proceso | Lee de `_hard/` | Extrae |
|---------|----------------|--------|
| **HardwareInfo** | `mcu.emic`, `pin_map.emic`, `family.emic` | Catalogo JSON de MCUs, perifericos, pines |
| **Validation** | `mcu.emic` (seccion Validation) | Reglas de validacion especificas del MCU |
| **Generate** | `mcu.emic` (seccion Generate) + perifericos `.emic` | Codigo C compilable en TARGET: |
| **PinInfo** | `pin_map.emic` | Mapa de pines con capacidades |
| **Discovery** | No lee `_hard/` | — |

### Detalle del flujo por proceso

**HardwareInfo**:
```
1. Motor define: system.process.HardwareInfo
2. Recorre: _hard/*/
   Para cada vendor:
     Para cada family:
       Lee {Family}.family.emic → info de familia
       Para cada model:
         Lee mcu.emic → EMIC:json(type = mcu) + seccion ifdef HardwareInfo
         Lee pins/pin_map.emic → EMIC:json(type = pin_map)
3. Resultado: JSON agregado → alimenta sdk.manifest.json
```

**Generate** (flujo normal de compilacion):
```
1. Motor define: system.process.Generate
2. PCB define: system.ucVendor, system.ucFamily, system.ucName
3. API/Driver invoca HAL:
   EMIC:setInput(DEV:_hal/UART/UART.emic, port=1, baud=9600, ...)
4. HAL ejecuta seccion ifdef Generate:
   EMIC:setInput(DEV:_hard/{ucVendor}/{ucFamily}/{ucName}/UART/UART.emic, ...)
5. Orquestador _hard ejecuta:
   EMIC:copy(inc/UART.h > TARGET:inc/UART1.h, port=1, ...)
   EMIC:copy(src/UART.c > TARGET:UART1.c, port=1, ...)
   EMIC:define(c_modules.UART1, UART1)
6. Resultado: Archivos .c/.h en TARGET listos para compilar
```

**Validation**:
```
1. Motor define: system.process.Validation
2. Lee mcu.emic del MCU del proyecto:
   → Obtiene EMIC:json(type = mcu) con perifericos disponibles
   → Obtiene EMIC:json(type = validation_rules) de seccion ifdef
3. Lee contratos HAL invocados por el proyecto:
   → Obtiene EMIC:json(type = peripheral) con funciones requeridas
4. Cruza: perifericos requeridos vs disponibles
5. Resultado: Reporte de compatibilidad (errores/warnings)
```

### Secciones `EMIC:ifdef` en archivos de periferico

Los archivos de periferico (`.emic`, `.h`, `.c`) tambien pueden tener
secciones condicionales, aunque es menos comun que en `mcu.emic`:

```
// UART.emic con seccion HardwareInfo
EMIC:ifdef system.process.HardwareInfo

EMIC:json(type = peripheral_implementation)
{
    "schema_version": "1.0",
    "peripheral": "UART",
    "mcu": ".{system.ucName}.",
    "implemented_functions": [
        "UART{port}_init",
        "UART{port}_bd",
        "UART{port}_sendByte",
        "UART{port}_readByte",
        "UART{port}_dataAvailable",
        "UART{port}_sendString"
    ],
    "implemented_optional": ["sendString"],
    "buffer_type": "FIFO",
    "uses_dma": false
}

EMIC:endif

// Resto del orquestador (solo en Generate)
EMIC:ifdef system.process.Generate
    // ... EMIC:copy, EMIC:define ...
EMIC:endif
```

---

## 13. Generacion de Pin Headers

### Flujo completo: pin_map.emic → setPinXX.h

```
┌─────────────────┐
│  pin_map.emic   │  Fuente de verdad (JSON)
│  (en _hard/)    │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  PCB asigna     │  EMIC:define(pins.led1, RB12)
│  nombres logicos│  EMIC:define(pins.uart1_tx, RD1)
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  pin_headers    │  Genera setPinXX.h por cada pin asignado
│  .emic          │  Cruza: nombre logico + capabilities del pin
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  TARGET:        │  setPinRB12.h → #define TRIS_led1 _TRISB12
│  inc/setPinXX.h │  setPinRD1.h  → #define TRIS_uart1_tx _TRISD1
└─────────────────┘
```

### pin_headers.emic (generador)

```
// @layer: hard
// @type: pin_header_generator

// Itera sobre todos los pines asignados en el PCB
EMIC:foreach(pins.*)

// Lee capabilities del pin desde pin_map.emic (resuelto en compile-time)
// Genera el header correspondiente
EMIC:setInput(DEV:_hard/.{system.ucVendor}./.{system.ucFamily}./.{system.ucName}./pins/setPin.{*}..emic, name=.{pins.*}.)

EMIC:endforeach
```

### Alternativa: generacion directa desde JSON

En el nuevo SDK, los `setPinXX.h` se pueden generar directamente desde
el JSON del `pin_map.emic` sin necesidad de archivos `.emic` intermedios
por pin. El motor de parseo lee el JSON, cruza con las asignaciones del
PCB y genera los headers.

Esta simplificacion elimina la necesidad de mantener decenas de archivos
`setPinXX.h` manuales por cada MCU.

---

## 14. Guia: Agregar un MCU Nuevo

### Checklist completo

```
[ ] 1. IDENTIFICAR ubicacion en la jerarquia
    Vendor: ________  Family: ________  Model: ________
    Ruta: _hard/{Vendor}/{Family}/{Model}/

[ ] 2. CREAR descriptor de familia (si no existe)
    _hard/{Vendor}/{Family}/{Family}.family.emic
    → JSON type = family con toolchain, ISR pattern, register access

[ ] 3. CREAR carpeta _shared/ de familia (si no existe)
    _hard/{Vendor}/{Family}/_shared/
    → FIFO, critical sections, utilities comunes

[ ] 4. CREAR descriptor de MCU
    _hard/{Vendor}/{Family}/{Model}/mcu.emic
    → JSON type = mcu con TODAS las secciones:
      - Seccion 1: metadata comun (siempre visible)
      - Seccion 2: ifdef HardwareInfo (detalles extendidos)
      - Seccion 3: ifdef Validation (reglas de validacion)
      - Seccion 4: ifdef Generate (macros de compilacion)

[ ] 5. CREAR pin map
    _hard/{Vendor}/{Family}/{Model}/pins/pin_map.emic
    → JSON type = pin_map con TODOS los pines del MCU
    → Incluir: capabilities, registers, pines especiales, funciones fijas

[ ] 6. IMPLEMENTAR System
    _hard/{Vendor}/{Family}/{Model}/System/
    ├── system.emic    → Orquestador
    ├── inc/system.h   → Clock, config pragmas, prototipos
    └── src/system.c   → SystemInit(), clock tree

[ ] 7. IMPLEMENTAR perifericos
    Por cada periferico que el MCU soporta:
    _hard/{Vendor}/{Family}/{Model}/{PERIFERICO}/
    ├── {periferico}.emic   → Orquestador (copy + define)
    ├── inc/{periferico}.h  → Prototipos con signatures C99
    └── src/{periferico}.c  → Implementacion con SFRs/ISRs

[ ] 8. VERIFICAR contratos
    Para cada periferico implementado:
    - Abrir _hal/{PERIFERICO}/{periferico}.emic
    - Verificar que TODAS las funciones "requires" estan implementadas
    - Verificar que las signatures coinciden exactamente

[ ] 9. VERIFICAR registros
    - Cada periferico registra c_modules.XXX
    - system.h esta en includes_head si es necesario
    - No se registran inits ni polls (responsabilidad de _api)

[ ] 10. EJECUTAR HardwareInfo
    - Regenerar sdk.manifest.json
    - Verificar que el nuevo MCU aparece en el catalogo

[ ] 11. NO MODIFICAR ningun archivo fuera de:
    _hard/{Vendor}/{Family}/{Model}/
    _hard/{Vendor}/{Family}/_shared/  (si se agrega codigo compartido)
    _hard/{Vendor}/{Family}/{Family}.family.emic  (si es nueva familia)
```

### Ejemplo: agregar ATmega328P

```
1. Identificacion:
   Vendor: Atmel    Family: AVR    Model: ATmega328P
   Ruta: _hard/Atmel/AVR/ATmega328P/

2. Familia AVR no existe → crear:
   _hard/Atmel/AVR/AVR.family.emic
   _hard/Atmel/AVR/_shared/   (vacio por ahora)

3. Crear mcu.emic:
   EMIC:json(type = mcu)
   {
       "schema_version": "1.0",
       "vendor": "Atmel",
       "family": "AVR",
       "model": "ATmega328P",
       "brief": "8-bit AVR, 16MHz, 32KB Flash, 2KB SRAM",
       "architecture": "AVR",
       "core": "ATmega",
       "bits": 8,
       "toolchain": {
           "compiler": "avr-gcc",
           "compiler_base": "gcc",
           "c_standard": "C99",
           "ide": "Arduino IDE / Microchip Studio",
           "header": "<avr/io.h>",
           "programmer": "AVRISP / USBasp",
           "linker_script": null
       },
       "memory": {
           "flash_kb": 32,
           "ram_kb": 2,
           "eeprom_bytes": 1024
       },
       "clock": {
           "max_frequency_mhz": 16,
           "internal_oscillator_mhz": 8,
           "pll_available": false,
           "fcy_formula": "F_CPU"
       },
       "peripherals": {
           "GPIO": { "available": true, "total_pins": 23, "ports": ["B", "C", "D"] },
           "UART": { "available": true, "instances": 1, "ports": [0], "max_baud": 2000000 },
           "SPI": { "available": true, "instances": 1, "ports": [0] },
           "I2C": { "available": true, "instances": 1, "ports": [0], "max_speed_khz": 400 },
           "ADC": { "available": true, "channels": 6, "resolution_bits": 10 },
           "Timer": { "available": true, "instances": 3, "timers": [0, 1, 2], "bits": [8, 16, 8] },
           "PWM": { "available": true, "channels": 6 }
       },
       "pin_remapping": { "available": false },
       "interrupts": { "total_vectors": 26, "priority_levels": 1, "nesting": false }
   }

4. Crear pin_map.emic con todos los pines del ATmega328P (DIP-28)

5. Implementar System:
   inc/system.h → #include <avr/io.h>, F_CPU, prototipos
   src/system.c → Configuracion de clock, fuses

6. Implementar UART:
   UART.emic → EMIC:copy con port=.{port}.
   inc/UART.h → Prototipos UART{port}_init, etc.
   src/UART.c → Acceso a UCSR0A, UDR0, ISR(USART_RX_vect)

7. Implementar GPIO, ADC, SPI, I2C, Timer, PWM...
```

---

## 15. Guia: Agregar un Periferico a un MCU Existente

### Flujo

```
1. VERIFICAR que existe contrato HAL en _hal/{PERIFERICO}/
   Si no existe → crearlo primero (ver Seccion 14.2 de la Propuesta)

2. CREAR carpeta del periferico:
   _hard/{vendor}/{family}/{model}/{PERIFERICO}/

3. CREAR orquestador:
   {periferico}.emic con EMIC:copy + EMIC:define(c_modules.XXX)

4. CREAR implementacion:
   inc/{periferico}.h  → Prototipos C99 puras
   src/{periferico}.c  → Implementacion con SFRs del vendor

5. ACTUALIZAR mcu.emic:
   Agregar entrada en "peripherals"
   Si el periferico tiene restricciones, agregar en seccion Validation

6. NO modificar ningun otro archivo del MCU ni de otros MCUs
```

### Ejemplo: agregar PWM al pic24FJ128GC006

```
1. Contrato PWM en _hal/PWM/PWM.emic → verificar que existe

2. Crear: _hard/Microchip/PIC24F/pic24FJ128GC006/PWM/

3. PWM.emic:
   EMIC:ifndef _HARD_PWM.{channel}._EMIC
   EMIC:define(_HARD_PWM.{channel}._EMIC, true)
   EMIC:copy(inc/PWM.h > TARGET:inc/PWM.{channel}..h, channel=.{channel}.)
   EMIC:copy(src/PWM.c > TARGET:PWM.{channel}..c, channel=.{channel}.)
   EMIC:define(c_modules.PWM.{channel}., PWM.{channel}.)
   EMIC:endif

4. inc/PWM.h + src/PWM.c con funciones del contrato:
   void PWM{channel}_init(void);
   void PWM{channel}_setDuty(uint16_t duty);
   void PWM{channel}_setFrequency(uint32_t freq);
   void PWM{channel}_start(void);
   void PWM{channel}_stop(void);

5. Actualizar mcu.emic:
   "PWM": {
       "available": true,
       "channels": 5,
       "features": ["output_compare"]
   }
```

---

## 16. Convenciones de Codigo

### 16.1. Signatures de funciones (frontera de portabilidad)

Todas las funciones expuestas por `_hard/` DEBEN tener signatures
C99 puras:

```c
// CORRECTO — C99 puro
void UART1_init(void);
void UART1_sendByte(uint8_t data);
uint8_t UART1_readByte(void);
uint16_t adc_readChannel(uint8_t channel);
void Timer1_setPeriod(uint32_t period_us);

// INCORRECTO — tipos vendor o extensiones en signature
void UART1_init(void) __attribute__((section(".text")));  // NO
BOOL UART1_dataAvailable(void);                           // NO: BOOL es tipo vendor
void UART1_sendByte(BYTE data);                           // NO: BYTE es tipo vendor
```

### 16.2. Tipos de datos

| Usar | No usar | Motivo |
|------|---------|--------|
| `uint8_t` | `unsigned char`, `BYTE`, `U8` | C99 estandar |
| `uint16_t` | `unsigned short`, `WORD`, `U16` | C99 estandar |
| `uint32_t` | `unsigned long`, `DWORD`, `U32` | C99 estandar |
| `int16_t` | `short`, `INT16` | C99 estandar |
| `bool` | `BOOL`, `_Bool`, `int` (como flag) | C99 `<stdbool.h>` |
| `size_t` | `unsigned int` (para tamaños) | C99 `<stddef.h>` |

### 16.3. Naming de funciones

```
{PERIFERICO}{instancia}_{accion}

UART1_init          → Periferico UART, instancia 1, accion init
UART1_sendByte      → camelCase para la accion
adc_readChannel     → Singleton: sin numero de instancia
Timer3_start        → Timer, instancia 3
PWM2_setDuty        → PWM, instancia 2
GPIO_setOutput      → GPIO es siempre singleton en naming
```

### 16.4. Naming de variables internas

```c
// Variables internas de _hard — NO visibles fuera
static uint8_t rxBuffer_1[64];        // Prefijo descriptivo + instancia
static volatile uint16_t rxHead_1;    // volatile para variables ISR
static fifo_t rxFifo_1;              // Struct del FIFO

// Variables exportadas (en header)
extern int16_t Buffer_entradas[];     // Buffer ADC (parte del contrato)
```

### 16.5. Include guards

```c
// inc/UART.h — usa placeholder de instancia
#ifndef UART_.{port}._H
#define UART_.{port}._H

#include <stdint.h>
#include <stdbool.h>

void UART.{port}._init(void);
void UART.{port}._sendByte(uint8_t data);
uint8_t UART.{port}._readByte(void);
uint8_t UART.{port}._dataAvailable(void);

#endif
```

### 16.6. Comentarios estructurados (M3)

```c
// @layer: hard
// @peripheral: UART
// @instance: .{port}.
// @implements: UART{port}_init, UART{port}_sendByte, UART{port}_readByte,
//              UART{port}_dataAvailable
// @contract: _hal/UART/UART.emic
// @mcu: .{system.ucName}.
```

---

## 17. Validacion y Verificacion

### 17.1. Verificacion de contratos (automatica)

El proceso Validation cruza los contratos HAL con las implementaciones
de `_hard/`:

```
Para cada periferico usado en el proyecto:
  1. Leer contrato: _hal/{PERIFERICO}/{periferico}.emic
     → Obtener "requires.functions"
  2. Leer implementacion: _hard/{vendor}/{family}/{model}/{PERIFERICO}/
     → Obtener funciones implementadas (de HardwareInfo o por scan)
  3. Verificar:
     - Toda funcion "requires" tiene implementacion → OK
     - Funcion faltante → ERROR: "MCU {model} no implementa {funcion}"
     - Signature no coincide → ERROR: "Signature mismatch: expected {X}, found {Y}"
```

### 17.2. Verificacion de mcu.emic (checklist)

| Check | Regla | Severidad |
|-------|-------|-----------|
| schema_version | Debe ser `"1.0"` | ERROR |
| vendor | Debe coincidir con nombre de carpeta padre | ERROR |
| family | Debe coincidir con nombre de carpeta padre | ERROR |
| model | Debe coincidir con nombre de carpeta actual | ERROR |
| toolchain.c_standard | Debe ser `"C99"` | ERROR |
| peripherals.keys | Cada key debe tener carpeta correspondiente en el modelo | WARNING |
| peripherals.UART.instances | Debe coincidir con numero de archivos generados | WARNING |

### 17.3. Verificacion de pin_map.emic (checklist)

| Check | Regla | Severidad |
|-------|-------|-----------|
| mcu field | Debe coincidir con model del mcu.emic | ERROR |
| total_io_pins | Debe coincidir con count de pins[] | WARNING |
| Cada pin.registers.tris | Debe ser un SFR valido del MCU | WARNING |
| capabilities vocabulario | Solo valores del vocabulario controlado | ERROR |
| Pines duplicados | No puede haber dos pins con mismo id o pin_number | ERROR |

### 17.4. Test de integracion (compile test)

El test final es la compilacion: dado un proyecto EMIC que use un MCU,
el proceso Generate debe producir codigo que compile sin errores con
el toolchain indicado en `mcu.emic`.

```
1. Crear proyecto de test con modulo minimo
2. Asignar MCU via PCB
3. Invocar cada periferico via HAL
4. Ejecutar Generate → TARGET:
5. Compilar con toolchain del MCU
6. Verificar: 0 errores de compilacion
7. Verificar: solo warnings conocidos/aceptables
```

---

## 18. Impacto en Capas Superiores

### 18.1. HAL (`_hal/`)

La capa HAL consume `_hard/` exclusivamente via routing:

```
// _hal/UART/UART.emic
EMIC:ifdef system.process.Generate
    EMIC:setInput(DEV:_hard/.{system.ucVendor}./.{system.ucFamily}./.{system.ucName}./UART/UART.emic,
                  port=.{port}.,baud=.{baud}.,BufferSize=.{BufferSize}.,driver=.{driver}.)
EMIC:endif
```

El HAL pasa parametros al orquestador de `_hard/` sin interpretar su
contenido. La capa `_hard/` es libre de usar esos parametros como
quiera (placeholders en `EMIC:copy`, condicionales en `.c`, etc.).

### 18.2. PCB (`_pcb/`)

El PCB define las tres macros que resuelven la ruta a `_hard/`:

```
EMIC:define(system.ucVendor, Microchip)
EMIC:define(system.ucFamily, PIC24F)
EMIC:define(system.ucName, pic24FJ128GC006)
```

Ademas, el PCB asigna pines con nombres logicos que se cruzan
con `pin_map.emic`:

```
// PCB asigna nombre logico → pin fisico
EMIC:setInput(DEV:_hal/pins/setPin.emic, name=uart1_tx, pin=RD1)
EMIC:setInput(DEV:_hal/pins/setPin.emic, name=uart1_rx, pin=RD0)
EMIC:setInput(DEV:_hal/pins/setPin.emic, name=led1, pin=RB12)
```

### 18.3. APIs y Drivers

Las APIs y drivers NO conocen la existencia de `_hard/`. Solo ven
las funciones expuestas por el contrato HAL:

```c
// Esto funciona identico en PIC24, STM32, AVR, ESP32:
UART1_init();
UART1_bd(9600);
UART1_sendByte('H');
uint8_t data = UART1_readByte();
```

### 18.4. Regla de init/poll (de MEMORY)

```
_hard/ registra:    c_modules.XXX  (para compilar)
_hard/ NO registra: inits.XXX ni polls.XXX

_api/ registra:     inits.XXX, polls.XXX  (llama hard init/poll internamente)

Ejemplo — Temperature API:
  init:  adc_init() + adc_addAnalogChannel()   ← llama a hard
  poll:  poll_adc()                             ← llama a hard
  Luego: lee Buffer_entradas[]                  ← variable de hard
```

### 18.5. DevAgent

El DevAgent se beneficia de la estructura `_hard/` para:

| Funcionalidad | Usa |
|--------------|-----|
| Listar MCUs disponibles | `sdk.manifest.json` → `mcu.emic` de cada MCU |
| Verificar compatibilidad | Contrato HAL vs `peripherals` de `mcu.emic` |
| Sugerir MCU por requisitos | Filtrar MCUs por perifericos requeridos |
| Generar documentacion | JSON de `mcu.emic` → fichas tecnicas |
| Detectar conflictos de pines | `pin_map.emic` + asignaciones del PCB |
| Agregar soporte para MCU nuevo | `family.emic` como guia + checklist de Seccion 14 |
| Generar implementacion de periferico | Leer contrato HAL + ISR pattern de familia |

---

## Apendice A: Vocabulario Controlado

### Nombres de perifericos (keys en `peripherals`)

Estos nombres son constantes del SDK. No se usan sinonimos.

| Nombre | NO usar | Periferico |
|--------|---------|------------|
| `UART` | Serial, USART, COM | Comunicacion serial asincrona |
| `SPI` | — | Serial Peripheral Interface |
| `I2C` | IIC, TWI | Inter-Integrated Circuit |
| `ADC` | A/D, AnalogIn | Conversor analogico-digital |
| `DAC` | D/A, AnalogOut | Conversor digital-analogico |
| `GPIO` | DigitalIO, Pin | General Purpose I/O |
| `Timer` | TMR, Counter | Temporizador/contador |
| `PWM` | OC, OutputCompare | Pulse Width Modulation |
| `DMA` | — | Direct Memory Access |
| `USB` | — | Universal Serial Bus |
| `CAN` | — | Controller Area Network |
| `Flash` | NVM, EEPROM_Emul | Flash self-write |
| `System` | Core, Init | Configuracion de sistema (clock, startup) |
| `WDT` | Watchdog | Watchdog Timer |
| `RTC` | — | Real-Time Clock |

### Capabilities de pines

| Capability | Significado |
|-----------|-------------|
| `digital_io` | GPIO digital |
| `analog_input` | Entrada ADC |
| `analog_output` | Salida DAC |
| `change_notification` | Interrupt on change |
| `remappable_io` | Pin remappable |
| `pwm_output` | Salida PWM fija |
| `open_drain` | Soporta open-drain |
| `5v_tolerant` | Tolerante a 5V |
| `high_current` | Salida de alta corriente |

### Categorias de funciones del contrato

| Categoria | Significado |
|-----------|-------------|
| `lifecycle` | init, deinit, poll |
| `config` | setBaudRate, setMode, setResolution |
| `io` | sendByte, readByte, write, read |
| `status` | dataAvailable, isBusy, getStatus |
| `interrupt` | setCallback, enableInterrupt |

---

## Apendice B: Matriz de Referencia — Implementacion por Familia

| Aspecto | PIC24/dsPIC (XC16) | PIC32 (XC32) | STM32 (arm-gcc) | AVR (avr-gcc) | ESP32 (xtensa-gcc) |
|---------|-------------------|-------------|----------------|-------------|-------------------|
| **Header vendor** | `<xc.h>` | `<xc.h>` | `stm32fXxx.h` | `<avr/io.h>` | `driver/uart.h` |
| **SFR access** | Bitfield: `U1MODEbits.X` | Bitfield + SET/CLR | Struct: `USART1->CR1` | Register: `UCSR0A` | Struct: `UART0.conf0` |
| **ISR decl** | `__attribute__((interrupt))` | `__attribute__((vector))` | `void X_IRQHandler()` | `ISR(X_vect)` | `IRAM_ATTR` + alloc |
| **ISR enable** | `IECxbits.X = 1` | `IECxSET = mask` | `NVIC_EnableIRQ()` | `UCSR0B \|= bit` | `esp_intr_alloc()` |
| **Clear flag** | `IFSxbits.X = 0` | `IFSxCLR = mask` | Read DR / manual | Auto | Manual |
| **Pin remap** | PPS (unlock/lock) | PPS | AFIO_REMAP | Ninguno | GPIO Matrix |
| **Global int off** | `__builtin_disi()` | `__builtin_disable_int()` | `__disable_irq()` | `cli()` | `portDISABLE_INT()` |
| **Bits** | 16 | 32 | 32 | 8 | 32 |
| **FIFO location** | `_shared/` | `_shared/` | `_shared/` | inline | inline o RTOS |
