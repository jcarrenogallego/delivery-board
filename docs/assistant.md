# Asistente de Delivery Board

El proyecto `backend/src/DeliveryBoard.Assistant` ofrece chat con Ollama u OpenAI y consulta el MCP Delivery Board mediante herramientas de lectura. Aspire inicia la API en `http://127.0.0.1:5092`; Swagger está en `/swagger` durante Development.

En Development, abrir `http://127.0.0.1:5092/` redirige a Swagger. Aspire transmite su entorno al asistente; en Production no se publican Swagger, OpenAPI ni la redirección de la raíz.

## Arranque con Aspire

Configura el nombre exacto de un modelo instalado en Ollama que admita herramientas. Ollama debe estar ejecutándose previamente; Aspire no lo instala ni descarga modelos.

```powershell
$env:Ollama__Model = 'nombre-del-modelo-local'
$env:OpenAI__Model = 'nombre-del-modelo-openai'
$env:OPENAI_API_KEY = 'tu-clave'
dotnet run --project backend/apphost/DeliveryBoard.AppHost
```

Para usar únicamente Ollama, omite la configuración de OpenAI. `/health`, Swagger y el proceso arrancan aunque falte configuración de proveedores. Aspire espera al MCP, que conserva sus dependencias actuales de base de datos y aprovisionamiento.

Los secretos de usuario del AppHost también permiten configurar `OpenAI:ApiKey`; se transmitirá al asistente mediante un parámetro secreto. No guardes claves reales en archivos versionados. Para ejecutar el asistente de forma independiente, configura sus variables o sus propios secretos de usuario y ejecuta `dotnet run --project backend/src/DeliveryBoard.Assistant` con el MCP ya disponible.

## Configuración

| Clave .NET / variable de entorno | Valor inicial o requisito |
| --- | --- |
| `Ollama:BaseUrl` / `Ollama__BaseUrl` | `http://localhost:11434` |
| `Ollama:Model` / `Ollama__Model` | Obligatorio al utilizar Ollama; admite herramientas |
| `OpenAI:Model` / `OpenAI__Model` | Obligatorio al utilizar OpenAI; admite herramientas |
| `OpenAI:ApiKey` / `OpenAI__ApiKey` | Obligatoria al utilizar OpenAI |
| `OPENAI_API_KEY` | Alternativa si `OpenAI:ApiKey` no está definida |
| `Mcp:Endpoint` / `Mcp__Endpoint` | `http://127.0.0.1:5091/mcp`; Aspire inyecta la dirección configurada del MCP |
| `Assistant:MaxToolRounds` / `Assistant__MaxToolRounds` | 8 rondas, rango admitido 1–64 |
| `Assistant:RequestTimeoutSeconds` / `Assistant__RequestTimeoutSeconds` | 180 segundos, positivo |
| `Assistant:Port` en AppHost | 5092 |

Se aplica la precedencia habitual de configuración .NET: el entorno reemplaza los archivos. La clave jerárquica OpenAI tiene prioridad sobre `OPENAI_API_KEY`, incluso si está vacía. El AppHost traslada explícitamente modelos, URL Ollama, límites y clave al hijo. El puerto MCP se configura mediante `Mcp:Port` en el AppHost.

## Consumir el streaming

La aplicación web utiliza el endpoint de la API backend descrito abajo. Los siguientes endpoints del asistente son para clientes que quieran consumir directamente SSE.

Ambos POST usan el mismo contrato, sustituyendo `ollama` por `openai` según corresponda. Desde PowerShell:

```powershell
curl.exe -N http://localhost:5092/api/assistant/ollama/chat `
  -H 'Content-Type: application/json' `
  -H 'Accept: text/event-stream' `
  --data-raw '{"messages":[{"role":"user","content":"Consulta y resume el trabajo pendiente en Delivery Board."}]}'
```

Cada petición incluye el historial completo de mensajes `user` y `assistant`, con texto no vacío y último mensaje `user`. El servidor no guarda conversaciones. La respuesta usa SSE con datos JSON en una sola línea por evento:

```text
event: delta
data: {"text":"Hay tres elementos pendientes."}

event: done
data: {"provider":"ollama","model":"modelo-configurado"}

```

Concatena el campo `text` de cada `delta`; `done` indica éxito. Si el flujo ya ha empezado y falla, termina con `error` (`code`, `message`, `traceId`) y no emite `done`. El cliente debe tratar el texto recibido como una respuesta parcial cuando falta `done`. No hay reintentos automáticos ni cambio de proveedor.

Antes de comenzar el flujo se usan ProblemDetails: `400` historial inválido, `503` configuración ausente o dependencia inaccesible, `502` fallo del proveedor/herramienta y `504` plazo agotado. La desconexión cancela las operaciones en curso. El límite de rondas produce `tool_round_limit`.

Swagger documenta ambos endpoints, pero su interfaz puede acumular la respuesta. Para observar fragmentos progresivos usa `curl -N` o `fetch` con lectura del cuerpo; `EventSource` no permite POST. Hay peticiones adicionales en `backend/src/DeliveryBoard.Assistant/DeliveryBoard.Assistant.http`.

## Datos y límites

Las herramientas disponibles son únicamente `get_database_schema` y `execute_read_query`, descubiertas mediante Streamable HTTP. No se llama a la API ni se conecta directamente a PostgreSQL. Las restricciones de consultas permanecen en el servidor MCP. Si una herramienta falla se termina la petición, sin fabricar una respuesta satisfactoria.

Antes de la primera llamada al modelo, el asistente obtiene obligatoriamente el esquema con `get_database_schema`. Lo incorpora como resultado de herramienta no confiable en el historial de esa petición, sin compartirlo entre conversaciones. Los resultados MCP de un único bloque de texto JSON se entregan al modelo como JSON estructurado, conservando los datos y evitando una envoltura con texto escapado. Si no puede obtener el esquema, no llama al modelo. Las instrucciones exigen usar los identificadores reales de PostgreSQL y entrecomillar todos los identificadores conservando su capitalización, especialmente columnas como `"Owner"`. Para nombres de personas se pide una coincidencia completa sin distinguir mayúsculas, evitando incluir nombres distintos mediante comodines.

Cuando una herramienta falla, el código público sigue siendo `mcp_tool_failure`. El error HTTP y el evento SSE `error` incluyen además un diagnóstico `mcp` con `tool`, `errorCode` y `kind` (`tool` o `transport`). Se conservan únicamente códigos conocidos: `query_rejected`, `query_cancelled`, `query_timeout`, `read_only_violation`, `database_error` e `internal_error`. Un contenido desconocido o inválido se normaliza a `tool_error`; una excepción de invocación a `invocation_error`; una herramienta de esquema ausente a `tool_not_available`.

Los logs propios del asistente registran ese diagnóstico seguro con el `traceId` de la respuesta. No registran argumentos, SQL, esquema, resultados, mensajes arbitrarios ni excepciones internas del MCP. La API backend mantiene sus errores genéricos y no publica estos detalles internos en el panel web.

Al elegir OpenAI se envían a OpenAI el historial y los resultados MCP necesarios para elaborar la respuesta. No se registran claves, SQL ni resultados de herramientas en los logs propios del asistente. Los mensajes de herramienta y el razonamiento interno no se publican en SSE; los resultados son datos no confiables para las instrucciones del modelo.

La API del asistente escucha exclusivamente en loopback, sin autenticación nueva ni CORS abierto; rechaza peticiones de navegadores con un Origin distinto. El endpoint MCP también debe ser local. Las consultas de la aplicación web pasan por la API backend, que conserva su configuración HTTP actual. No se incluyen persistencia de conversaciones, modelos predeterminados ni gestión de Ollama.

## Consultar desde la aplicación web

El panel **Pregunta al tablero**, situado entre las métricas y el tablero, permite escribir una pregunta libre, elegir **Ollama (local)** u **OpenAI** y pulsar **Consultar** o Enter. La respuesta completa aparece debajo. Cada pregunta es independiente y sustituye el resultado anterior; no se guarda historial.

El navegador llama únicamente a `POST /api/assistant/query` de la API backend mediante el proxy del frontend:

```json
{
  "provider": "ollama",
  "question": "¿Qué tareas están pendientes?"
}
```

La API valida y recorta la pregunta, la envía como un único mensaje `user` al asistente y reúne los eventos `delta`. Solo devuelve éxito tras recibir `done`, sin realizar otra llamada al modelo:

```json
{
  "answer": "Hay tres tareas pendientes.\nUna está bloqueada por una dependencia.",
  "provider": "ollama",
  "model": "modelo-configurado"
}
```

El texto conserva sus saltos de línea y se muestra como texto seguro. Durante la espera se bloquean el envío y el selector. Si falla, la pregunta se conserva para reintentar manualmente. No se presentan respuestas parciales como resultados satisfactorios.

| Configuración de la API backend | Valor inicial |
| --- | --- |
| `AssistantClient:BaseUrl` / `AssistantClient__BaseUrl` | `http://127.0.0.1:5092` |
| `AssistantClient:TimeoutSeconds` / `AssistantClient__TimeoutSeconds` | 210 segundos |

El plazo cubre conexión y lectura completa, y debe ser mayor que el plazo del asistente (180 segundos por defecto). Aspire inyecta la dirección según `Assistant:Port` y transmite `AssistantClient:TimeoutSeconds` si se configura en el AppHost. No se necesita copiar claves ni modelos a la API o al frontend.

La API no espera al asistente durante el arranque: el MCP ya espera a la API para su aprovisionamiento, por lo que añadir esa espera crearía un ciclo. Una consulta temprana puede devolver `503` hasta que el asistente esté listo. Las otras operaciones del tablero siguen disponibles.

Los errores de este endpoint usan ProblemDetails: `400` pregunta o proveedor inválidos, `503` asistente/proveedor no disponible, `504` tiempo agotado y `502` respuesta inválida o incompleta. Un evento `error`, JSON inválido, ausencia de `done` o respuesta vacía descarta el texto acumulado. La desconexión del navegador se propaga a la API y de ahí al asistente.

Swagger de la API backend documenta el contrato JSON. Hay ejemplos en `backend/src/DeliveryBoard.Api/Assistant.http`. Tras actualizar el código, reinicia Aspire para cargar la integración y el proxy nuevos.

## Verificación

Las pruebas del asistente usan modelos simulados y un servidor MCP de fixtures en un puerto local efímero; no requieren credenciales, Ollama ni PostgreSQL. Cubren contratos HTTP, configuración, SSE, límites, cancelación y consultas mediante herramientas. Las pruebas de la API alojan el endpoint de forma aislada con HTTP simulado, sin ejecutar migraciones; incluyen fragmentación UTF-8, errores, truncamiento, cancelación y timeout. El frontend y el proxy tienen pruebas con fixtures para envío, resultados, errores y desconexión. La prueba real de cada modelo requiere su configuración y se realiza aparte.
