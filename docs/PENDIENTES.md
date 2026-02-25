# EMIC_DevAgent - Pendientes de Implementacion

## Fases completadas

| Fase | Descripcion | Estado |
|------|------------|--------|
| **Fase 0** | Separacion Core/CLI, interfaces de abstraccion, DI | Completado |
| **Fase 0.5** | Integracion EMIC.Shared (MediaAccess, TreeMaker, SdkScanner, MetadataService) | Completado |
| **Fase 1** | Servicios LLM (ClaudeLlmService) + Compilacion (EmicCompilationService, CompilationErrorParser) | Completado |
| **Fase 2** | Templates (Api, Driver, Module) + Validadores (4) + ValidationService + RuleValidatorAgent | Completado |
| **Fase 3** | Agentes (7) + OrchestrationPipeline — orquestacion completa | Completado |
| **Fase 4** | ITemplateEngine impl + CLI cleanup — zero stubs, pipeline E2E listo | Completado |
| **Fase 5** | SourceMapper (retropropagacion) + BackwardsCompatibilityValidator | Completado |

---

## Completados por categoria

### 1. Servicios LLM (4/4) ✅

- [x] `ClaudeLlmService.GenerateAsync()` — llamada a Claude API
- [x] `ClaudeLlmService.GenerateWithContextAsync()` — generacion con contexto
- [x] `ClaudeLlmService.GenerateStructuredAsync<T>()` — respuestas JSON tipadas
- [x] `LlmPromptBuilder.Build()` — system prompts especializados por tipo de agente

### 2. Compilacion (2/2) ✅

- [x] `EmicCompilationService` — wrapper sobre CompilerService de EMIC.Shared (XC16)
- [x] `CompilationErrorParser.Parse()` — parsear output del compilador a errores estructurados

### 3. Templates (4/4) ✅

- [x] `ApiTemplate.GenerateAsync()` — genera .emic/.h/.c para APIs
- [x] `DriverTemplate.GenerateAsync()` — genera archivos de driver
- [x] `ModuleTemplate.GenerateAsync()` — genera generate.emic, deploy.emic, m_description.json
- [x] `TemplateEngineService` — ITemplateEngine que delega a los 3 templates especializados

### 4. Validadores (5/5) ✅

- [x] `LayerSeparationValidator` — APIs no acceden a TRIS/LAT/PORT directamente
- [x] `NonBlockingValidator` — detecta while bloqueantes y `__delay_ms()`
- [x] `StateMachineValidator` — verifica patron switch(state) con variables static y timeouts
- [x] `DependencyValidator` — verifica que EMIC:setInput apunte a archivos existentes, sin ciclos
- [x] `ValidationService.ValidateAllAsync()` — orquesta los 4 validadores y combina resultados

### 5. Agentes (8/8) ✅

- [x] **OrchestratorAgent** — clasifica intent, desambigua via IUserInteraction, coordina workflow top-down/bottom-up
- [x] **AnalyzerAgent** — escanea SDK, encuentra componentes reutilizables, identifica gaps, verifica intercambiabilidad de drivers
- [x] **ApiGeneratorAgent** — genera APIs nuevas con LLM + patrones HAL, EMIC:ifdef guards
- [x] **DriverGeneratorAgent** — genera drivers con LLM, patron ADS1231, funciones intercambiables
- [x] **ModuleGeneratorAgent** — genera modulos completos propagando opciones top-down
- [x] **ProgramXmlAgent** — genera program.xml y userFncFile desde funciones extraidas
- [x] **CompilationAgent** — invoca compilador, retry loop, backtracking basico, auto-fix includes
- [x] **RuleValidatorAgent** — delega a los 4 validadores especializados

### 6. Pipeline (1/1) ✅

- [x] `OrchestrationPipeline.ExecuteAsync()` — ejecuta pasos registrados en secuencia con condiciones y status tracking

### 7. SDK Services (ya implementados en Fase 0.5) ✅

- [x] `SdkScanner` — escanea _api, _drivers, _modules, _hal via MediaAccess
- [x] `SdkPathResolver` — resuelve volumenes virtuales
- [x] `EmicFileParser` — parsea .emic con TreeMaker modo Discovery
- [x] `MetadataService` — lee/escribe .emic-meta.json

### 8. CLI (Fase 4) ✅

- [x] `Program.cs` — limpio, sin stubs, con output de archivos generados y validacion
- [x] `ITemplateEngine` registrado en DI via `TemplateEngineService`

---

## Pendientes por implementar

### 9. Sistema de retropropagacion de errores ⚠️ Requiere migracion

**Prioridad: Alta** — Backend+Frontend implementados en EMIC.Shared/EMIC.Web.IDE. SourceMapper del DevAgent usa estrategia obsoleta.

**Implementacion real (EMIC.Shared — en produccion):**
- [x] TreeMaker.addToCodigo() genera archivos `.map` TSV separados (`SYS:map/TARGET/xxx.c.map`)
- [x] Formato: linea N del .map = linea N del TARGET, contenido TSV `originLine\toriginFile\tcomment`
- [x] CompilerService.ResolveSourceLocation() resuelve errores via lookup directo en .map
- [x] Module.AppendCompilerException() agrega `source-file`/`source-line` al XML de errores
- [x] Frontend compiler-exception.js navega al archivo fuente en SDK Editor
- [x] Documentado en `INFO/DEV-APP/BUILD-SYSTEM/SOURCE_MAP_ERROR_RETROPROPAGATION.md`

**SourceMapper del DevAgent (estrategia obsoleta — pendiente de migrar):**
- [x] `SourceMapper` — inserta markers `// @source:` en codigo generado cada 10 lineas
- [x] `SourceMapper.MapError()` — escaneo hacia arriba buscando marker mas cercano + offset
- [x] Integrado con CompilationAgent — usa SourceMapper + CompilationErrorParser para backtracking

**Problemas de la estrategia `// @source:`:**
1. Modifica el codigo generado (inserta comentarios que desplazan numeros de linea)
2. Resolucion imprecisa (bloques de 10 lineas vs mapeo exacto linea-a-linea del .map)
3. No aprovecha los archivos .map que TreeMaker ya genera durante EMIC:Generate

**Pendiente — Migrar SourceMapper a usar .map files:**
- [ ] Refactorizar SourceMapper para leer archivos `.map` TSV de `SYS:map/TARGET/`
- [ ] Eliminar logica de InsertMarkers() (ya no es necesaria)
- [ ] ResolveErrorLocation() debe usar lookup directo por indice (mapIndex = targetLine - 1)
- [ ] Mantener fallback por filename matching para casos sin .map disponible
- [ ] Actualizar CompilationAgent para no llamar InsertSourceMarkers() pre-compilacion

### 10. Agente de conversion de versiones

**Prioridad: Media** — Necesario para migrar SDKs legacy.

- [ ] Definir alcance: que cambia entre versiones del SDK
- [ ] Diseñar reglas de conversion (rename funciones, actualizar parametros, adaptar patrones)
- [ ] Implementar como agente nuevo o como modo del AnalyzerAgent

### 11. Generacion de modulos y proyectos de test

**Prioridad: Media** — Necesario para verificacion fisica.

- [ ] TestModuleGeneratorAgent (o extension de ModuleGeneratorAgent)
  - Genera modulo en categoria `TestModule` que ejercita todas las funciones/eventos del componente
  - Incluye program.xml con llamadas a cada funcion y handlers para cada evento
- [ ] TestProjectCreator (o extension del workflow del OrchestratorAgent)
  - Crea proyecto EMIC completo que instancia el modulo de test
  - Incluye Data tab con variables de prueba
  - Sigue el patron del tutorial TUTORIAL_YOGURTERA_CONTROLLER.md

### 12. Pipeline configurable y extensibilidad multi-SDK

**Prioridad: Media** — Deseable para extensibilidad, pero sin sobrediseñar.

- [ ] Evaluar que parametros del pipeline conviene externalizar a JSON (habilitar/deshabilitar agentes, maxRetries, etc.)
- [ ] Implementar configuracion basica donde aporte valor real, evitando abstracciones innecesarias
- [ ] Preparar estructura para que en el futuro se puedan agregar pipelines para otros SDKs (Web, backend, mobile)

### 13. Retrocompatibilidad en capas inferiores ✅

**Prioridad: Alta** — Implementado en Fase 5.

- [x] `BackwardsCompatibilityValidator` — verifica EMIC:ifdef/#ifdef guards en .emic/.h/.c
- [x] Valida .emic: optional EMIC:define (events/polls) debe tener EMIC:ifdef correspondiente
- [x] Valida .h/.c: funciones no-core fuera de ifdef blocks → Warning
- [x] Core functions (init, poll, *_init, *_poll) se excluyen automaticamente
- [ ] Implementar en ApiGeneratorAgent: verificar que nuevos features usen parametros condicionales
- [ ] Implementar en DriverGeneratorAgent: EMIC:ifdef guards para funcionalidad nueva

---

## Resumen numerico

| Categoria | Items | Estado |
|-----------|:-----:|--------|
| LLM | 4 | ✅ Completado |
| Compilacion | 2 | ✅ Completado |
| Templates | 4 | ✅ Completado |
| Validadores | 6 | ✅ Completado |
| Agentes | 8 | ✅ Completado |
| Pipeline | 1 | ✅ Completado |
| SDK Services | 4 | ✅ Completado |
| CLI cleanup | 2 | ✅ Completado |
| Retropropagacion | 3+5 | ⚠️ Backend OK, SourceMapper pendiente migrar |
| Conversion versiones | 3 | Pendiente |
| Test modules/projects | 2 | Pendiente |
| Pipeline configurable | 3 | Pendiente |
| Retrocompatibilidad | 4+2 | ✅ Validator + 2 pendientes |
| **Total** | **53** | **38 completados / 15 pendientes** |

---

## Orden sugerido para pendientes restantes

1. **Migrar SourceMapper a .map files** — eliminar `// @source:` markers, usar TSV lookup directo
2. ~~**Retrocompatibilidad (validator)**~~ ✅ — BackwardsCompatibilityValidator implementado
3. **Retrocompatibilidad (generators)** — integrar verificacion en ApiGenerator/DriverGenerator
4. **Test modules/projects** — extension de ModuleGenerator + ProjectCreator
5. **Pipeline configurable** — esquema JSON + loader
6. **Conversion de versiones** — agente especializado
7. **Testing E2E** — prompt -> codigo -> compilacion -> correccion

---

## Documentacion de referencia

- **Reglas de workflow detalladas**: `docs/WORKFLOW.md`
- **Tutorial de referencia (proyectos)**: `INFO/DEV-APP/CLI/TUTORIAL_YOGURTERA_CONTROLLER.md`
- **SDK de prueba**: `PIC_XC16/` (cambios libres, sin restricciones de produccion)

---

*Ultima actualizacion: 2026-02-25*
