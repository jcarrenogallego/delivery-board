# Delivery Board Agent

Base de la sesión15: un agente **ReAct con `createAgent`**, consultas a PostgreSQL mediante el MCP existente y creación de tareas mediante DeliveryBoard.Api. OpenAI por defecto; Ollama configurable. No usa ni depende del asistente .NET anterior.

## Qué está construido

- Conversación, consultas reales y creación de tareas: las tres capacidades del laboratorio anterior.
- Esquema real → SQL generado por el agente → resultado MCP → corrección de SQL cuando falla → respuesta.
- Máximo dos llamadas SQL por petición, memoria de conversación y límite de llamadas al modelo.
- Aclaración de datos con `interrupt()` y confirmación humana antes del POST de creación.
- Tres skills `SKILL.md`, cargadas completas o bajo demanda.
- Pruebas sencillas de consola, prueba offline sin modelos y comprobación TypeScript.
- Tags y metadata preparados para LangSmith, pero **tracing desactivado inicialmente**.

No incorpora interfaz web, no publica servidor MCP propio y no implementa las otras acciones de la API (borrar, completar, reasignar…). Crear tareas sí modifica datos cuando el usuario lo confirma. No hay reintento automático de escrituras.

## Arranque

Desde esta carpeta, con Node22.18 o superior recomendado:

```powershell
npm ci --ignore-scripts
Copy-Item .env.example .env
npm run prueba:config
npm run prueba:skills
npm run check
npm run prueba:offline
```

Si ya existe `.env`, no lo reemplaces. La preparación inicial deja una copia **sin clave**. En el equipo se observaron dos runtimes Node (22.14 en npm y 25.8 en otra consola); las pruebas offline deben indicar cuál estás usando. Node22.14 ejecuta estos `.ts` con su soporte de strip-types, pero se recomienda actualizar a22.18+.

## Antes de probar con OpenAI: punto de parada

**No se ha llamado a OpenAI y no se debe copiar ninguna clave de los proyectos anteriores.**

Con DeliveryBoard.Api y DeliveryBoard.Mcp arrancados, verifica primero:

```powershell
npm run prueba:conexion
npm run prueba:mcp
```

Esto solo comprueba servicios, descubre tools, lee esquema y ejecuta SELECT1. No necesita OpenAI ni crea tareas. La API y el MCP son procesos distintos; Docker con PostgreSQL y la API no bastan si el MCP está apagado. Los puertos del ejemplo son5202 (API) y5091 (MCP); adapta `.env` a los endpoints reales de tu ejecución/Aspire.

**Aquí nos detenemos hasta que decidas probar la clave.** Cuando autorices la prueba, configura localmente una clave nueva en `.env` (no la pegues en el chat):

```dotenv
MODEL_PROVIDER=openai
OPENAI_API_KEY=
OPENAI_MODEL=gpt-4o-mini
```

Después, y solo entonces:

```powershell
npm run prueba:modelo
npm run prueba:tools
npm run preguntar -- "¿Cuántas tareas pendientes tiene Ana?"
npm start
```

Estos últimos comandos sí llaman al proveedor configurado y, con OpenAI, generan consumo. La prueba tools usa una herramienta inocua y no escribe datos. Para probar creación real, usa `npm start`, escribe una petición de tarea y revisa sus campos antes de contestar sí. `/nueva` inicia un hilo limpio y `salir` cierra la conexión MCP.

## Proveedor local

```dotenv
MODEL_PROVIDER=ollama
OLLAMA_MODEL=qwen3:4b
SKILLS_MODE=inline
```

`qwen3:4b` era el modelo de la **guía** de sesión14; su código actual utiliza OpenAI. Puedes experimentar con `qwen2.5-coder:1.5b`, usado en el laboratorio anterior, cambiando OLLAMA_MODEL. Primero ejecuta `prueba:modelo` y `prueba:tools`: **si no emite tool_calls válidas, más instrucciones no aseguran arreglarlo**. No hay fallback automático de Ollama a OpenAI y no se descargan modelos automáticamente.

## Grafo

```text
Usuario → modelo (createAgent)
             ├── obtener_esquema → MCP ────────────┐
             ├── consultar_sql → MCP ─────────────┤
             ├── pedir_aclaracion → interrupt ────┤
             ├── crear_tarea → confirmar → API ───┤
             └── cargar_skill → SKILL.md ─────────┤
                    ▲                            │
                    └──── observaciones → modelo ┘
                                      └── respuesta final
```

```powershell
npm run grafo
```

No abre conexiones. Muestra Mermaid del grafo real de `createAgent`, incluidos los nodos de límites. Para ver las actualizaciones de una consulta real, tras autorizar el proveedor:

```powershell
npm run prueba:stream -- "¿Qué tareas tiene Ana?"
```

## Guías

- [Tutorial fichero por fichero](TUTORIAL.md).
- [Skills: estándar y elección de implementación](docs/SKILLS.md).
- [Estado del laboratorio revisado](docs/REVISION.md).
- [Comprobaciones y pruebas pendientes](docs/VERIFICACION.md).
- [Punto de partida para sesión15](docs/SESION15.md).

Las skills orientan al modelo; las validaciones y permisos reales siguen en tools, MCP/PostgreSQL y API. La confirmación y los límites son una base mínima, no una afirmación de seguridad de producción. No se garantiza deduplicación entre sesiones ni idempotencia en el endpoint de creación.


## Diagnosticar esperas con Ollama

`npm run preguntar` imprime progreso (inicio/fin de modelo y herramientas) mientras trabaja y explica qué límite se agotó, sin mostrar un stack sin contexto. `REQUEST_TIMEOUT_MS` es el presupuesto total del run; se mantiene el valor configurado por el usuario. Consulta [DIAGNOSTICO.md](docs/DIAGNOSTICO.md).
