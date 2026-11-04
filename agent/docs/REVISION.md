# Revisión del punto de partida

Fecha: 2 de noviembre de 2026. Solo lectura sobre los proyectos de referencia; no se han modificado sus fuentes, modelos, dependencias ni base de datos. La carpeta DeliveryBoard.Assistant quedó fuera de la inspección.

## Laboratorio langgraph/delivery-board

Grafo explícito con clasificador para conversación, consultas y crear_tarea. Consulta mediante cargar_esquema → generar_sql → ejecutar_sql → responder; hasta dos intentos SQL. Extrae tareas con modelo, usa interrupt cuando faltan datos y hace POST a la API. Checkpointer MemorySaver y consola readline. El proveedor del laboratorio era Ollama.

Se conservan las tres capacidades, el ciclo de corrección, las aclaraciones y la memoria. No se trasladan sus clientes globales conectados al importar, ni su regex para entrecomillar SQL: las conexiones se crean/cerran explícitamente y el SQL se escribe a partir del esquema real.

## sesion14

Incluye grafo ReAct manual y agente createAgent, guía paso a paso, ejemplos de consola y estado de mensajes. La guía presenta qwen3:4b; las fuentes actuales instancian ChatOpenAI con gpt-4o-mini.

Se encontró configuración de credencial hardcodeada. No se copió ni se utilizó. El usuario debe rotarla; este proyecto lee OPENAI_API_KEY del entorno y entrega .env vacío.

Las dependencias declaraban core1.3.0, pero npm exige core^1.3.1 para openai1.8.0 y langchain1.6.0. El proyecto nuevo fija core1.3.2 sin --force ni --legacy-peer-deps.

## Contratos reales de Delivery Board

- API: GET /health; POST /api/work-items con title, owner, priority opcional; prioridad predeterminada Medium. Máximos de persistencia: título160 y owner80. El ID viene de la API.
- MCP: /mcp por HTTP, con get_database_schema y execute_read_query. /health independiente.
- La consulta devuelve columns, rows, rowCount, truncated y truncationReason. Los errores pueden llegar como isError y/o JSON error, no como filas vacías.
- PostgreSQL utiliza work_items y columnas PascalCase con comillas. Status y Priority están persistidos como texto.
- IsBlocked se calcula en GetDashboardUseCase: Pending y al menos una dependencia que no esté entre las completadas. No existe como columna de work_items.
- La API tiene más acciones, pero no se exponen al agente para mantener el alcance conversación/consulta/creación del laboratorio actual.

## Decisiones para la nueva base

Un solo createAgent con tools pequeñas, modelos intercambiables, prompts en módulo y skills en archivos. Nada de clasificador LLM para elegir nombres de nodos. Confirmación antes de una escritura, sin reintentar POST, y sin prometer idempotencia que el endpoint no ofrece.

Proyecto separado, sin integración automática en el frontend ni cambios de Aspire. Conectar con la API y MCP existentes basta para este laboratorio por consola.
