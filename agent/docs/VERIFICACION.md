# Verificación de la preparación

Fecha: 2 de noviembre de 2026. Estado de entrega: **preparado para probar con una clave nueva de OpenAI, todavía sin realizar esa prueba**.

## Ejecutado

| Comprobación | Resultado observado |
|---|---|
| Instalación npm local, sin scripts de instalación | Correcta, sin --force ni --legacy-peer-deps |
| npm run check | TypeScript sin errores |
| npm run prueba:config | OpenAI seleccionado, clave pendiente, URL API5202 y MCP5091 |
| npm run prueba:skills | Tres SKILL.md válidas y catálogo disponible |
| npm run prueba:offline | ReAct y tools reales con modelo/MCP/API simulados; consulta con corrección, límite SQL, historial, carga de skill bajo demanda, límite de modelo, pausas/reanudación, una creación simulada, cancelación y resultado incierto de escritura |
| npm run grafo | Mermaid real del agente y middleware, sin llamar al modelo |
| npm run prueba:conexion | API /health200 y MCP /health200 |
| npm run prueba:mcp | Descubiertas execute_read_query y get_database_schema; esquema de public.work_items leído y SELECT1 devuelve1 |

La lectura real confirmó Id uuid, Title varchar160, Owner varchar80, Priority/Status varchar20, CreatedAt timestamptz, PrerequisiteIds uuid[] y Comment text.

Los tests de preparación de integración no leyeron listas de tareas ni escribieron fixtures. No se crearon, modificaron ni borraron datos reales. No se arrancaron ni alteraron contenedores o servicios existentes.

## Pendiente hasta autorización

- Llamada de prueba a OpenAI con una clave nueva guardada localmente en .env.
- Consulta en lenguaje natural de extremo a extremo con el modelo real.
- Creación confirmada de una tarea de laboratorio y comprobación de su respuesta real.
- Comparación de Ollama con OpenAI y de skills inline/progressive con modelos reales. No se ha descargado ni invocado ningún modelo local.
- Activación de LangSmith y preparación de demos de guardrails de sesión15.

Un guion simulado verifica conexiones entre piezas y efectos controlados; **no demuestra precisión ni fiabilidad del LLM real**.

## Ajustes técnicos encontrados

- core1.3.0 no satisface los peers publicados de openai1.8.0 y langchain1.6.0. Se fijó core1.3.2 y se conservó el lockfile generado.
- Para langchain1.6.0 se usa la API pública agente.drawMermaid(); getGraphAsync está tipada como interna.
- Las opciones de stream no aceptan tags en los tipos de esta versión; los tags/metadata de la aplicación se incluyen en invoke de la consola.
- Node22.14 ejecutó los scripts npm en la preparación; Node25.8 también está presente en otra consola. Recomendación de uso:22.18+.

No se copiaron claves de otros proyectos ni se incluyó ninguna en los documentos. El archivo .env entregado no contiene una clave real.
