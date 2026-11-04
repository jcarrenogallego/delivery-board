# Organización de Delivery Board

## Componentes

- `backend/`: solución .NET existente, API, PostgreSQL y servidor MCP.
- `frontend/`: interfaz existente.
- `agent/`: copia independiente del agente TypeScript con su propio package-lock.
- `evals/`: espacio documental reservado para la próxima sesión.

Los proyectos conservan sus propias dependencias y configuración. No se han
modificado los contratos funcionales, la orquestación existente ni las rutas
de sus servicios. No se ha añadido todavía un workflow o Compose de CI.

## Agente

Desde `agent/`, utiliza su README y TUTORIAL. Las rutas de esas guías se
interpretan respecto a la raíz del agente. Si una guía menciona entrar en la
carpeta histórica `delivery-board-agent`, en este monorepo corresponde a `agent/`.
Las variantes de Deep Agents y Phoenix son guías opcionales: incluirlas no
significa que se hayan instalado o activado.

No se ha copiado `.env` ni `node_modules`. Crea `.env` desde `.env.example`
y proporciona tus propias claves mediante variables de entorno o secretos
locales; nunca las escribas en appsettings versionados. La clave OpenAI del
AppHost se puede proporcionar con la variable de entorno `OPENAI_API_KEY`.

## Preparación para CI

El futuro workflow descargará este repositorio, preparará una base de datos
aislada y arrancará los servicios necesarios. Los evals no utilizarán datos
de producción. Las claves de proveedores se guardarán en GitHub Secrets.
La interfaz web no será necesaria para evaluar el agente por MCP y API.

## Publicación

El repositorio público parte de un snapshot limpio en `main`, sin incorporar
el historial anterior del laboratorio. Los documentos heredados describen
también etapas previas y no prueban que los checks se hayan ejecutado sobre
esta versión. Esta preparación no incluye compilación ni pruebas.
