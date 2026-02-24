# EMIC_DevAgent - Pendientes de Implementacion

## Fases completadas

| Fase | Descripcion | Estado |
|------|------------|--------|
| **Fase 0** | Separacion Core/CLI, interfaces de abstraccion, DI | Completado |
| **Fase 0.5** | Integracion EMIC.Shared (MediaAccess, TreeMaker, SdkScanner, MetadataService) | Completado |

---

## Pendientes por categoria

### 1. Servicios LLM (4 stubs)

- [ ] `ClaudeLlmService.GenerateAsync()` — llamada a Claude API
- [ ] `ClaudeLlmService.GenerateWithContextAsync()` — generacion con contexto
- [ ] `ClaudeLlmService.GenerateStructuredAsync<T>()` — respuestas JSON tipadas
- [ ] `LlmPromptBuilder.Build()` — system prompts especializados por tipo de agente

**Archivos:** `Core/Services/Llm/ClaudeLlmService.cs`, `Core/Services/Llm/LlmPromptBuilder.cs`

### 2. Compilacion (2 stubs)

- [ ] `ICompilationService` — wrapper sobre CompilerService de EMIC.Shared (XC16)
- [ ] `CompilationErrorParser.Parse()` — parsear output del compilador a errores estructurados

**Archivos:** `Cli/Program.cs` (factory stub), `Core/Services/Compilation/CompilationErrorParser.cs`

### 3. Templates (3 stubs)

- [ ] `ApiTemplate.GenerateAsync()` — genera .emic/.h/.c para APIs
- [ ] `DriverTemplate.GenerateAsync()` — genera archivos de driver
- [ ] `ModuleTemplate.GenerateAsync()` — genera generate.emic, deploy.emic, m_description.json

**Archivos:** `Core/Services/Templates/ApiTemplate.cs`, `DriverTemplate.cs`, `ModuleTemplate.cs`

### 4. Validadores (5 stubs)

- [ ] `LayerSeparationValidator` — APIs no acceden a TRIS/LAT/PORT directamente
- [ ] `NonBlockingValidator` — detecta while bloqueantes y `__delay_ms()`
- [ ] `StateMachineValidator` — verifica patron switch(state) con variables static y timeouts
- [ ] `DependencyValidator` — verifica que EMIC:setInput apunte a archivos existentes, sin ciclos
- [ ] `ValidationService.ValidateAllAsync()` — orquesta los 4 validadores y combina resultados

**Archivos:** `Core/Agents/Validators/`, `Core/Services/Validation/ValidationService.cs`

### 5. Agentes (8 stubs existentes)

- [ ] **OrchestratorAgent** — clasifica intent, desambigua via IUserInteraction, coordina workflow top-down/bottom-up
- [ ] **AnalyzerAgent** — escanea SDK, encuentra componentes reutilizables, identifica gaps, verifica intercambiabilidad de drivers
- [ ] **ApiGeneratorAgent** — genera APIs nuevas respetando naming conventions (funciones intercambiables entre drivers)
- [ ] **DriverGeneratorAgent** — genera drivers con retrocompatibilidad (parametros condicionales para features nuevos)
- [ ] **ModuleGeneratorAgent** — genera modulos completos propagando opciones top-down
- [ ] **ProgramXmlAgent** — genera program.xml y archivos asociados para logica de integradores
- [ ] **CompilationAgent** — invoca compilador, parsea errores, retropropaga correcciones al SDK (max 5 retries)
- [ ] **RuleValidatorAgent** — delega a los 4 validadores especializados

**Archivos:** `Core/Agents/`

### 6. Pipeline (1 stub)

- [ ] `OrchestrationPipeline.ExecuteAsync()` — ejecuta pasos registrados en secuencia

**Archivos:** `Core/Agents/Base/OrchestrationPipeline.cs`

---

## Tareas nuevas (identificadas en revision de workflow)

### 7. Sistema de retropropagacion de errores

**Prioridad: Alta** — Sin esto, el CompilationAgent no puede corregir errores eficientemente.

- [ ] Diseñar sistema de marcado de codigo expandido (TreeMaker marca cada linea con archivo:linea origen)
- [ ] Implementar parser de marcas en output compilado → archivo SDK fuente
- [ ] Integrar con CompilationAgent para corregir archivo correcto del SDK

**Problema**: El compilador reporta errores en el codigo expandido (TARGET), no en el SDK fuente.
El agente necesita saber que archivo .emic/.c/.h del SDK genero la linea con error.

**Posible enfoque**: Insertar comentarios `// @source: DEV:_api/xxx/yyy.c:42` en el codigo generado,
luego parsear la posicion del error y buscar la marca mas cercana.

### 8. Agente de conversion de versiones

**Prioridad: Media** — Necesario para migrar SDKs legacy.

- [ ] Definir alcance: que cambia entre versiones del SDK
- [ ] Diseñar reglas de conversion (rename funciones, actualizar parametros, adaptar patrones)
- [ ] Implementar como agente nuevo o como modo del AnalyzerAgent

### 9. Generacion de modulos y proyectos de test

**Prioridad: Media** — Necesario para verificacion fisica.

- [ ] TestModuleGeneratorAgent (o extension de ModuleGeneratorAgent)
  - Genera modulo en categoria `TestModule` que ejercita todas las funciones/eventos del componente
  - Incluye program.xml con llamadas a cada funcion y handlers para cada evento
- [ ] TestProjectCreator (o extension del workflow del OrchestratorAgent)
  - Crea proyecto EMIC completo que instancia el modulo de test
  - Incluye Data tab con variables de prueba
  - Sigue el patron del tutorial TUTORIAL_YOGURTERA_CONTROLLER.md

### 10. Retrocompatibilidad en capas inferiores

**Prioridad: Alta** — Regla transversal que afecta a ApiGenerator, DriverGenerator, y validadores.

- [ ] Implementar en ApiGeneratorAgent: verificar que nuevos features usen parametros condicionales
- [ ] Implementar en DriverGeneratorAgent: EMIC:ifdef guards para funcionalidad nueva
- [ ] Implementar en DependencyValidator: verificar que modulos existentes no se rompan
- [ ] Documentar patron estandar de retrocompatibilidad (EMIC:ifdef + #ifdef)

---

## Resumen numerico

| Categoria | Items | Depende de |
|-----------|:-----:|------------|
| LLM | 4 | Claude API + prompts nuevos |
| Compilacion | 2 | Wrapper sobre EMIC.Shared |
| Templates | 3 | Implementacion nueva + patrones del SDK |
| Validadores | 5 | Implementacion nueva (analisis de codigo C) |
| Agentes | 8 | Todo lo anterior + LLM |
| Pipeline | 1 | Agentes |
| Retropropagacion | 3 | Diseño nuevo + TreeMaker |
| Conversion versiones | 3 | Definicion de reglas |
| Test modules/projects | 2 | ModuleGenerator + APIs del servidor |
| Retrocompatibilidad | 4 | Validadores + Generators |
| **Total** | **35** | |

---

## Orden sugerido de implementacion

1. **LLM** — sin esto los agentes no pueden generar codigo
2. **Compilacion** — wrapper liviano sobre EMIC.Shared
3. **Retropropagacion** — diseño del sistema de marcado (necesario antes de CompilationAgent)
4. **Templates** — patrones parametrizados basados en archivos reales del SDK
5. **Validadores** — reglas EMIC incluyendo retrocompatibilidad
6. **Agentes** — orquestacion completa (requiere todo lo anterior)
7. **Test modules/projects** — extension de ModuleGenerator + ProjectCreator
8. **Conversion de versiones** — agente especializado
9. **Pipeline + testing E2E** — prompt -> codigo -> compilacion -> correccion

---

## Documentacion de referencia

- **Reglas de workflow detalladas**: `docs/WORKFLOW.md`
- **Tutorial de referencia (proyectos)**: `INFO/DEV-APP/CLI/TUTORIAL_YOGURTERA_CONTROLLER.md`
- **SDK de prueba**: `PIC_XC16/` (cambios libres, sin restricciones de produccion)

---

*Ultima actualizacion: 2026-02-24*
