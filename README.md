# EMIC_DevAgent

Agente de IA para desarrollo SDK EMIC - CLI multi-agente en C# (.NET 8)

## Descripcion

EMIC_DevAgent es un agente de IA tipo CLI que genera codigo para el SDK EMIC. Utiliza una arquitectura multi-agente con Claude como LLM para:

- Analizar prompts del usuario y clasificar intents
- Escanear el SDK existente para encontrar componentes reutilizables
- Generar APIs, drivers y modulos siguiendo los patrones del SDK
- Validar reglas EMIC (separacion de capas, no-blocking, state machines)
- Compilar con XC16 y corregir errores automaticamente

## Requisitos

- .NET 8 SDK
- XC16 Compiler (para compilacion)
- API key de Claude (Anthropic)

## Build

```bash
dotnet build
```

## Tests

```bash
dotnet test
```

## Uso

```bash
dotnet run --project src/EMIC_DevAgent.Cli
```

Configurar `SdkPath` en `src/EMIC_DevAgent.Cli/appsettings.json` antes de ejecutar.

## Arquitectura

Ver [docs/architecture.md](docs/architecture.md) para el diagrama completo de agentes.

## Conceptos EMIC

Ver [docs/EMIC_Conceptos_Clave.md](docs/EMIC_Conceptos_Clave.md) para los conceptos fundamentales del SDK.
