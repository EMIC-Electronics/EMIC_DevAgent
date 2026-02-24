# EMIC DevAgent - Reglas de Workflow

## 1. Tipos de solicitud del usuario

El usuario puede pedir:
- **Nuevo modulo** — completo con generate.emic, deploy.emic, m_description.json
- **Nuevo PCB** — header de placa con definicion de pines y system.ucName
- **Nueva API** — capa de abstraccion (.emic, .h, .c) sobre drivers
- **Nuevo driver** — implementacion para chip externo (.emic, .h, .c)
- **Nuevo hard/hal** — funciones de bajo nivel para un periferico del MCU
- **Adaptacion/conversion** — modificar o portar un modulo, API o driver existente

## 2. Desambiguacion del prompt

El prompt del usuario puede ser ambiguo. El agente DEBE:
- Hacer preguntas concretas para obtener una definicion clara y univoca
- Ofrecer recomendaciones basadas en lo que ya existe en el SDK
- Confirmar entendimiento antes de proceder al plan
- Ejemplo: "quiero medir temperatura" → ¿que sensor? ¿que placa? ¿necesita comunicacion por bus?

## 3. Diseño top-down, ejecucion bottom-up

**Fase de diseño (top-down):**
1. Definir el modulo completo (que APIs, drivers, PCB necesita)
2. Identificar APIs existentes vs nuevas necesarias
3. Identificar drivers existentes vs nuevos necesarios
4. Identificar HAL/hard existentes vs nuevos necesarios

**Fase de ejecucion (bottom-up):**
1. Crear/modificar hard (codigo especifico del MCU)
2. Crear/modificar HAL (abstraccion de periferico)
3. Crear/modificar drivers (chip externo)
4. Crear/modificar APIs (capa de alto nivel)
5. Crear/modificar modulo (generate.emic que conecta todo)
6. Crear proyecto de test y compilar

**Regla de contexto por capa**: Al implementar bottom-up, las capas inferiores NO deben conocer
el contexto completo del proyecto. El agente solo proporciona a cada capa las definiciones y
el contexto que le corresponden. Esto fuerza un diseño generico y reutilizable, no ad-hoc.
- Al generar hard/hal: solo recibe nombre de periferico, pines, y parametros electricos
- Al generar driver: solo recibe interfaz del chip y parametros HAL que necesita
- Al generar API: solo recibe nombre del driver y parametros funcionales
- El modulo es el unico que tiene vision completa del proyecto

## 4. Retrocompatibilidad en capas inferiores

**Regla critica**: Solo crear hard/hal cuando NO exista el codigo necesario.

Cuando sea necesario agregar funciones a codigo existente en capas inferiores:
- **Condicionar** el codigo nuevo a la existencia de un parametro explicito
- Ejemplo: `useHandshake=true` habilita handshake en UART, el codigo viejo lo ignora
- El objetivo es que modulos existentes sigan compilando sin cambios
- Patron en .emic:
  ```
  EMIC:ifdef(useHandshake)
    // codigo nuevo para handshake
  EMIC:endif
  ```
- Patron en .c/.h:
  ```c
  #ifdef USE_HANDSHAKE
    // codigo nuevo
  #endif
  ```

## 5. Intercambiabilidad de drivers (naming convention)

Drivers que cumplen la misma funcion DEBEN tener funciones con nombres identicos a nivel API.
Esto permite intercambiarlos cambiando solo el parametro `driver=` en generate.emic.

**Ejemplo — sensores de temperatura:**
- Todos exponen `getTemperature()` independientemente del chip (LM35, DS18B20, DHT11)
- La API recibe `driver=LM35` y delega al driver correcto

**Ejemplo — USB:**
```
EMIC:setInput(DEV:_api/Wired_Communication/USB/USB_API.emic,driver=MCP2200,port=1,BufferSize=512,baud=9600,frameLf=\n)
```
- La API pasa `driver=.{driver}.` al layer de drivers
- El driver MCP2200 implementa la interfaz que la API espera

**Mecanismo**: La API usa `.{driver}.` en el path del EMIC:setInput para seleccionar el driver:
```
EMIC:setInput(DEV:_drivers/USB/.{driver}./.{driver}..emic, ...)
```

## 6. Propagacion de opciones (module → API → driver → HAL → hard)

Todas las opciones se definen en el modulo (generate.emic) y se propagan hacia abajo:
- Cada capa recibe parametros via `.{param}.` en EMIC:setInput
- Cada capa resuelve los que necesita y pasa el resto hacia abajo
- `system.ucName` es especial — lo define el PCB y lo usa el HAL para rutear a `_hard/`

**Cadena tipica:**
```
generate.emic (driver=MCP2200, port=1, baud=9600)
  → API.emic (usa port, pasa driver+port+baud al driver)
    → Driver.emic (usa driver, pasa port+baud al HAL)
      → HAL.emic (rutea a _hard/.{system.ucName}./...)
        → HARD.emic (genera .c/.h con port+baud sustituidos)
```

## 7. Testing fisico — modulo de test

Siempre que sea posible, el sistema debe:
- Crear un **modulo de test** en la categoria `TestModule` (o `Test`)
- El modulo de test implementa TODAS las funciones y eventos del componente creado
- Esto permite verificacion fisica en hardware real
- El modulo de test incluye program.xml con llamadas a todas las funciones

## 8. Proyecto de prueba

Siempre que sea posible:
- Crear un **proyecto EMIC de prueba** que instancie el modulo de test
- El proyecto contiene: program.xml + Data tab con variables de prueba
- Permite compilar y flashear para pruebas en placa real
- Referencia de como crear proyectos: `INFO/DEV-APP/CLI/TUTORIAL_YOGURTERA_CONTROLLER.md`
- La carpeta de proyectos del usuario se solicita en el momento de implementacion

## 9. Retropropagacion de errores de compilacion

**Problema**: El codigo que se compila es una EXPANSION del SDK, no el codigo fuente directo.
Los errores del compilador (XC16) apuntan a lineas del codigo expandido, no al archivo .emic/.c/.h
original del SDK.

**Solucion requerida**: Sistema de marcado y retropropagacion:
1. Durante la expansion (TreeMaker), marcar cada linea generada con su archivo fuente y linea
2. Cuando el compilador reporta un error en linea N del expandido, usar las marcas para
   identificar el archivo SDK origen
3. Propagar la correccion al archivo correcto del SDK

**Estado**: Pendiente de diseño detallado (ver tarea en PENDIENTES.md)

## 10. Notas operativas

### SDK de prueba (PIC_XC16)
- El repositorio `PIC_XC16` es un SDK de prueba donde se pueden hacer cambios libremente
- Se pueden romper reglas que en produccion no se podrian romper
- Usar para desarrollo iterativo y testing del DevAgent

### Modulos inconsistentes
- Algunos modulos, APIs y drivers del SDK no estan funcionando correctamente
- Si el agente encuentra un modulo con muchas inconsistencias, debe informar al usuario
  para que decida si eliminarlo
- No intentar arreglar modulos muy rotos sin autorizacion explicita

### Agente de conversion de versiones (pendiente)
- Queda pendiente la definicion de un agente especializado en convertir
  componentes de versiones viejas del SDK a la version actual
- Esto incluye: renombrar funciones, actualizar parametros, adaptar patrones

## 11. Pipeline y agentes configurables

Es deseable que los pipelines y agentes sean reconfigurables sin modificar codigo,
pero hay que buscar el equilibrio entre complejidad y modularidad. No sobrediseñar:
- Configuracion via archivo JSON cuando aporte valor real (ej: habilitar/deshabilitar agentes, ajustar parametros)
- Evitar abstracciones innecesarias que compliquen el codigo sin beneficio claro
- Si un cambio es infrecuente, es aceptable que requiera modificar codigo

**Ejemplo de configuracion:**
```json
{
  "pipeline": "embedded-c",
  "steps": [
    { "agent": "Analyzer", "enabled": true },
    { "agent": "ApiGenerator", "enabled": true },
    { "agent": "DriverGenerator", "enabled": true },
    { "agent": "ModuleGenerator", "enabled": true },
    { "agent": "RuleValidator", "enabled": true },
    { "agent": "Compilation", "enabled": true, "maxRetries": 5 }
  ],
  "language": "C",
  "compiler": "XC16"
}
```

## 12. Extensibilidad a otros tipos de SDK

La arquitectura del DevAgent esta diseñada para ser extensible a otros dominios:
- La estructura de SDK (capas: api, drivers, hal, hard, modules) es universal
- Lo que cambia entre dominios es el **lenguaje** y el **compilador/toolchain**
- Ejemplos futuros:
  - **SDK Web Frontend**: JS/TS, webpack/vite, componentes React/Vue
  - **SDK Web Backend**: C#/Python/Node, APIs REST, middleware
  - **SDK Mobile**: Kotlin/Swift, SDKs nativos
- El pipeline se configura por tipo de SDK (ver seccion 11)
- Los agentes base (Analyzer, Orchestrator, RuleValidator) son reutilizables
- Los agentes especificos (ApiGenerator, Compilation) se especializan por lenguaje
- La estructura de carpetas (_api, _drivers, _hal, etc.) se mantiene como convencion
