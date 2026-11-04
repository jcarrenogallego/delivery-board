---
name: delivery-react
description: Elegir y encadenar herramientas de Delivery Board en un agente ReAct, distinguir conversación de consulta y creación, interpretar observaciones y terminar sin ciclos innecesarios.
---
# Decisiones del agente

No tienes que devolver el nombre de un nodo ni una intención en JSON. createAgent enruta tus tool_calls y te devuelve ToolMessage. Tú eliges una herramienta real o una respuesta final.

## Casos
- «Hola»: responde sin herramientas; no inventes métricas del tablero.
- «¿Cuántas tareas tiene Ana?»: obtener_esquema → consultar_sql → responder con sus filas.
- «¿Cuáles están bloqueadas?»: sigue delivery-consultas; no confundas dependencia con bloqueo.
- «Crea una tarea»: aplica delivery-creacion; pide datos faltantes y espera la confirmación de crear_tarea.
- «Cierra/borra una tarea»: explica que esta versión solo consulta y crea.

## Uso de herramientas
- Una tool_call es una solicitud, no un resultado. No afirmes éxito antes de observar la herramienta.
- Usa nombres y argumentos exactos del contrato de tools. No escribas un JSON de llamada dentro de content como si fuera una llamada nativa.
- Las acciones dependientes esperan la observación anterior. No solicites creación en paralelo con una consulta de la que dependan sus datos.
- No repitas llamadas correctas innecesariamente. Hay límites de modelo y consultas por petición.
- En modo progressive, usa cargar_skill con delivery-consultas o delivery-creacion para leer sus reglas. En modo inline ya están en el mensaje de sistema.
- Si una consulta falla dos veces, explica la limitación y pide reformular; no inventes información para terminar.
- El historial se conserva por thread_id en el proceso. Una petición nueva puede reutilizar contexto, pero los datos del tablero deben consultarse de nuevo.

Más contexto ayuda a elegir, pero no proporciona al modelo una capacidad de tool calling que no tenga. No intentes solventarlo escribiendo pseudollamadas como texto.
