# Conceptos Clave del SDK EMIC

Este documento resume los conceptos fundamentales del ecosistema EMIC que el agente de IA debe conocer para generar codigo correcto.

---

## 1. Arquitectura en Capas

El SDK EMIC sigue una arquitectura de abstraccion en capas estricta:

```
Aplicacion (App)
    |
    v
_modules  -->  Modulos completos (hardware + firmware + config)
    |
    v
_api      -->  APIs de alto nivel (LED, Relay, Timer, UART, etc.)
    |
    v
_drivers  -->  Drivers para chips externos (ADS1231, SX1276, etc.)
    |
    v
_hal      -->  Hardware Abstraction Layer (GPIO, SPI, I2C, UART, ADC, PWM)
    |
    v
_hard     -->  Registros del microcontrolador (TRIS, LAT, PORT, etc.)
```

### Jerarquia de Dependencias
- `_modules` pueden usar `_api` y `_drivers`
- `_api` solo pueden usar `_hal` (NUNCA `_hard` directamente)
- `_drivers` solo pueden usar `_hal`
- `_hal` es la unica capa que accede a `_hard`

---

## 2. Reglas No Negociables

### 2.1 Separacion de Capas
- Las APIs **NUNCA** acceden registros directos (`TRISAbits`, `LATAbits`, `PORTAbits`)
- Solo usan funciones HAL: `HAL_GPIO_SetOutput()`, `HAL_GPIO_Write()`, `HAL_SPI_Transfer()`, etc.
- Esto permite portabilidad entre microcontroladores

### 2.2 No-Blocking
- **PROHIBIDO** usar `while()` bloqueantes o `__delay_ms()` en APIs
- Se usa `getSystemMilis()` para medir tiempo + state machines
- El loop principal (`main.c`) debe ejecutarse sin bloqueos
- Patron correcto:
```c
static unsigned long lastTime = 0;
if (getSystemMilis() - lastTime >= PERIOD) {
    lastTime = getSystemMilis();
    // accion periodica
}
```

### 2.3 State Machines
- Operaciones complejas (multi-step) usan patron `switch(state)`:
```c
static int state = 0;
switch(state) {
    case 0: // Inicio
        startOperation();
        state = 1;
        break;
    case 1: // Esperando
        if (operationComplete()) state = 2;
        if (timeout()) state = 0; // reset con timeout
        break;
    case 2: // Completado
        processResult();
        state = 0;
        break;
}
```
- Variables de estado son `static` dentro de la funcion
- Siempre incluir manejo de timeout

---

## 3. Sistema EMIC-Codify

EMIC-Codify es el lenguaje de scripting que controla la generacion de codigo.

### 3.1 Macros y Variables de Template
- Formato: `.{nombre}` - Se sustituyen durante generacion
- Ejemplo: `.{name}` se reemplaza por el nombre de instancia del componente
- Ejemplo: `.{pin}` se reemplaza por el pin GPIO asignado
- Se declaran con `EMIC:setInput` y se usan en archivos .h y .c

### 3.2 Comandos EMIC Principales

| Comando | Funcion |
|---------|---------|
| `EMIC:setInput` | Incluye otro archivo .emic como dependencia |
| `EMIC:copy` | Copia un archivo de DEV: a TARGET: aplicando macros |
| `EMIC:define` | Agrega entrada a un diccionario del sistema |
| `EMIC:ifdef` / `EMIC:endif` | Compilacion condicional basada en funciones usadas |

### 3.3 Volumenes Logicos

| Volumen | Descripcion |
|---------|-------------|
| `DEV:` | Directorio raiz del SDK de desarrollo (fuentes originales) |
| `TARGET:` | Directorio de destino donde se genera el firmware |
| `SYS:` | Archivos del sistema EMIC |
| `USER:` | Archivos del usuario/integrador |

### 3.4 Ejemplo de Archivo .emic (led.emic)
```
EMIC:setInput DEV:/_hal/GPIO/gpio.emic
EMIC:setInput DEV:/_drivers/SystemTimer/systemTimer.emic

EMIC:copy DEV:/_api/Indicators/LEDs/inc/led.h TARGET:/inc/.{name}.h
EMIC:copy DEV:/_api/Indicators/LEDs/src/led.c TARGET:/src/.{name}.c

EMIC:define inits .{name}._init();
EMIC:define polls .{name}._poll();
EMIC:define main_includes #include ".{name}.h"
EMIC:define c_modules .{name}.c
```

---

## 4. Diccionarios del Sistema

Los diccionarios son listas que se expanden en `main.c` durante la generacion:

| Diccionario | Donde se expande | Proposito |
|-------------|-----------------|-----------|
| `inits` | `main()` antes del loop | Inicializacion de cada componente |
| `polls` | Dentro del `while(1)` | Polling periodico de componentes |
| `main_includes` | Al inicio de `main.c` | `#include` de headers generados |
| `c_modules` | Configuracion del compilador | Archivos .c a compilar |

### Ejemplo de main.c expandido:
```c
// main_includes expandido:
#include "led1.h"
#include "relay1.h"
#include "timer1.h"

void main(void) {
    initSystem();

    // inits expandido:
    led1._init();
    relay1._init();
    timer1._init();

    while(1) {
        // polls expandido:
        led1._poll();
        timer1._poll();
    }
}
```

---

## 5. Flujo de Desarrollo EMIC

```
1. Discovery   -->  Buscar componentes disponibles en el ecosistema
2. Editor      -->  Configurar modulo: seleccionar PCB, APIs, parametros
3. Generate    -->  EMIC-Codify procesa .emic files y genera firmware
4. Compile     -->  XC16 compila el proyecto generado
```

### 5.1 Discovery
- Busqueda de recursos por tags (categoria, microcontrolador, interfaz)
- El ecosistema es colaborativo: desarrolladores publican modulos

### 5.2 Editor
- Interfaz visual para configurar modulos
- Seleccion de PCB, asignacion de pines, parametros

### 5.3 Generate
- El transcriptor EMIC procesa generate.emic
- Resuelve dependencias recursivamente (setInput)
- Copia archivos aplicando macros (.{name}, .{pin}, etc.)
- Registra funciones en diccionarios (inits, polls, etc.)
- Merge de archivos generados en TARGET:

### 5.4 Compile
- XC16 compila el proyecto generado
- Los errores se mapean a los archivos fuente originales

---

## 6. Patrones de Archivos por Componente

### 6.1 API (ejemplo: LEDs)
```
_api/
    Indicators/
        LEDs/
            led.emic          # Script de generacion
            inc/
                led.h         # Header template con EMIC:ifdef condicionales
            src/
                led.c         # Implementacion con macros .{name}, .{pin}
```

### 6.2 Driver (ejemplo: ADS1231)
```
_drivers/
    ADC/
        ADS1231/
            ADS1231.emic      # Script de generacion
            inc/
                ADS1231.h     # Header con prototipos
            src/
                ADS1231.c     # Implementacion usando HAL
```

### 6.3 Modulo (ejemplo: HRD_LoRaWan)
```
_modules/
    Wireless_Communication/
        HRD_LoRaWan/
            System/
                generate.emic     # Script principal de generacion
                deploy.emic       # Script de despliegue
            m_description.json    # Metadata del modulo
```

---

## 7. Patrones de Codigo Canonicos

### 7.1 Header (.h) con Condicionales EMIC
```c
#ifndef _COMPONENT_.{name}_H
#define _COMPONENT_.{name}_H

void .{name}._init(void);

EMIC:ifdef usedFunction .{name}._state
void .{name}._state(int value);
EMIC:define inits .{name}._init();
EMIC:endif

EMIC:ifdef usedFunction .{name}._poll
void .{name}._poll(void);
EMIC:define polls .{name}._poll();
EMIC:endif

#endif
```

### 7.2 Implementacion (.c) No-Bloqueante
```c
#include ".{name}.h"
#include "gpio.h"
#include "systemTimer.h"

void .{name}._init(void) {
    HAL_GPIO_SetOutput(.{pin});
    HAL_GPIO_Write(.{pin}, 0);
}

void .{name}._state(int value) {
    switch(value) {
        case 0: HAL_GPIO_Write(.{pin}, 0); break;  // OFF
        case 1: HAL_GPIO_Write(.{pin}, 1); break;  // ON
        case 2: HAL_GPIO_Toggle(.{pin}); break;     // TOGGLE
    }
}

static unsigned long lastTime = 0;
static int blinkState = 0;

void .{name}._poll(void) {
    if (getSystemMilis() - lastTime >= period) {
        lastTime = getSystemMilis();
        // logica de blink no-bloqueante
    }
}
```

### 7.3 Script .emic de Generacion
```
EMIC:setInput DEV:/_hal/GPIO/gpio.emic
EMIC:setInput DEV:/_drivers/SystemTimer/systemTimer.emic

EMIC:copy DEV:/_api/Category/Name/inc/name.h TARGET:/inc/.{name}.h
EMIC:copy DEV:/_api/Category/Name/src/name.c TARGET:/src/.{name}.c

EMIC:define inits .{name}._init();
EMIC:define polls .{name}._poll();
EMIC:define main_includes #include ".{name}.h"
EMIC:define c_modules .{name}.c
```

---

## 8. Principios de Diseno EMIC

1. **Modularidad**: Cada componente es autocontenido con su .emic, .h y .c
2. **Reutilizacion**: Los componentes se instancian multiples veces con diferentes .{name}
3. **Colaboracion**: Ecosistema compartido de componentes validados
4. **Estandarizacion**: Todos los componentes siguen los mismos patrones y convenciones
5. **SOLID en C**: Principios de diseno aplicados a codigo embebido
   - Single Responsibility: cada archivo tiene una sola responsabilidad
   - Open/Closed: extensible via macros sin modificar fuentes
   - Dependency Inversion: APIs dependen de HAL abstracto, no de hardware concreto
