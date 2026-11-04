# Sesión 15: observar nuestro agente local en LangSmith

Vamos a conectar **Delivery Board Agent** con LangSmith para ver qué ocurre dentro de cada petición: llamadas al modelo, herramientas, tiempos y errores.

**El agente sigue ejecutándose en tu ordenador.** No vamos a desplegarlo en la nube. Lo que se envía a LangSmith es la telemetría que habilitemos.

```text
Consola → agente local → modelo / MCP / API
                 │
                 └── trazas → LangSmith → navegador
```

| Paso | Qué haces | Qué observas |
|---|---|---|
| 0 | Comprobar el proyecto | El agente funciona antes de añadir tracing |
| 1 | Preparar LangSmith en la web | Cuenta, destino y clave |
| 2 | Configurar `.env` | Activar trazas y elegir privacidad |
| 3 | Ejecutar una petición | Primera traza |
| 4 | Abrirla en la web | Árbol modelo/herramientas |
| 5 | Comparar ejecuciones | Tiempos, errores, tags y tokens |
| 6 | Desactivar tracing | Volver al estado inicial |

**Dinámica:** configura un paso, ejecuta y mira el resultado. No necesitas modificar `createAgent` para empezar.

---

## Paso 0: comprobar el punto de partida

Desde la carpeta `projects`:

```powershell
cd delivery-board-agent
npm run prueba:config
npm run prueba:conexion
npm run prueba:mcp
```

API y MCP deben responder. La última prueba muestra esquema y SELECT 1, sin crear tareas.

Elige en `.env` el proveedor que ya hayas comprobado: `MODEL_PROVIDER=ollama` u `openai`. LangSmith funciona con ambos. La clave de **LangSmith es diferente de la de OpenAI**; usar Ollama no exige una clave OpenAI, pero el servicio LangSmith sí necesita su propia credencial.

Si Ollama todavía agota el timeout, revisa [el diagnóstico local](docs/DIAGNOSTICO.md). Tracing no acelera el modelo ni arregla sus decisiones. Puedes observar un fallo, pero no presentarlo como una consulta correcta.

### ¿Hace falta instalar otra librería?

```powershell
npm ls langsmith
```

El proyecto ya tiene el SDK como dependencia de LangChain. En la revisión había `langsmith@0.11.7`. Para estas instrucciones no hace falta añadir imports nuevos ni instalar paquetes.

---

## Paso 1: preparar LangSmith en la web

### 1.1 Entrar y elegir workspace

Abre [LangSmith](https://smith.langchain.com), entra en tu cuenta y elige el workspace del laboratorio. Comprueba su región: no se deduce del idioma ni del país de tu ordenador.

### 1.2 Elegir dónde guardar las trazas

La web actual puede organizar trazas de dos formas. **Utiliza una sola de estas rutas:**

**A. Si ves agentes y entornos:** usa la opción de observar un agente existente, normalmente `New Agent` → `Observability` / `Trace existing agent`. Crea un registro con nombre «Delivery Board sesión 15» e identificador `delivery-board-agent-sesion15`, o copia el ID que hayas elegido. Después seleccionaremos el entorno **Local**.

Ese registro organiza trazas: no sube tu código ni sustituye a `createAgent`. No es el flujo Builder ni un despliegue. Reutiliza el registro si ya existe.

**B. Si tu workspace organiza trazas por proyectos:** utiliza un tracing project llamado `delivery-board-agent-sesion15`. La configuración será `LANGSMITH_PROJECT`, en lugar de ID de agente y entorno.

Los rótulos pueden cambiar con la versión de la web. Lo importante es identificar **workspace + agente/entorno o proyecto**, no crear ambos destinos a la vez.

### 1.3 Crear una clave de LangSmith

En la configuración del workspace, abre **API Keys** y crea una clave apropiada para enviar trazas. Guárdala localmente. Si no tienes permisos para crear el registro o la clave, pide al administrador que lo prepare.

No la pegues en el chat ni en archivos TypeScript. No necesitas una clave de OpenAI para crear la de LangSmith. Revisa también el plan y las condiciones de retención/consumo de tu cuenta antes de enviar datos.

---

## Paso 2: configurar `.env`

**Edita el `.env` existente; no lo reemplaces ni dupliques variables.** Conserva proveedor, modelo y URLs del proyecto.

### 2.1 Activar tracing con privacidad inicial

Añade o modifica:

```dotenv
LANGSMITH_TRACING=true
LANGCHAIN_TRACING_V2=true
LANGSMITH_API_KEY=TU_CLAVE_LANGSMITH
LANGCHAIN_CALLBACKS_BACKGROUND=true

LANGSMITH_HIDE_INPUTS=true
LANGSMITH_HIDE_OUTPUTS=true
```

Sustituye `TU_CLAVE_LANGSMITH` solo en tu archivo privado. Pon ambos flags de tracing en el mismo valor para no mezclar una configuración moderna activa con una antigua desactivada.

Ocultar entradas/salidas permite empezar viendo estructura y tiempos sin enviar esos cuerpos. **No convierte toda la telemetría en anónima:** revisa también nombres, metadata y errores. Nunca introduzcas claves en preguntas, títulos de tareas o tags.

### 2.2 Elegir región

Configura el endpoint API correspondiente al workspace, no la URL del navegador. Ejemplos:

```dotenv
# Workspace en US:
LANGSMITH_ENDPOINT=https://api.smith.langchain.com
```

```dotenv
# Workspace en EU, en lugar del anterior:
LANGSMITH_ENDPOINT=https://eu.api.smith.langchain.com
```

Conserva **una sola** línea activa. Para otra región o una instalación propia, copia el endpoint de su configuración. Si la clave puede acceder a varios workspaces, añade `LANGSMITH_WORKSPACE_ID` con el ID correcto.

### 2.3 Elegir destino: A o B

**A. Workspace con agentes y entornos:**

```dotenv
LANGSMITH_AGENT_ID=delivery-board-agent-sesion15
LANGSMITH_AGENT_ENVIRONMENT=local
```

Usa el identificador real creado en la web, no el nombre visible. Elimina o comenta `LANGSMITH_PROJECT`, `LANGCHAIN_PROJECT` y `LANGCHAIN_SESSION` si estaban configuradas.

**B. Workspace basado en proyectos:**

```dotenv
LANGSMITH_PROJECT=delivery-board-agent-sesion15
```

En este caso no configures `LANGSMITH_AGENT_ID` ni `LANGSMITH_AGENT_ENVIRONMENT`. No mezcles los dos sistemas.

### 2.4 Reiniciar la aplicación

Cada comando npm nuevo lee `.env`. Si tienes `npm start` abierto, escribe `salir` y vuelve a arrancarlo. Las variables ya definidas en PowerShell tienen prioridad sobre `.env`: si un cambio no se aplica, revisa la terminal y elimina solo la variable heredada que esté interfiriendo.

En `src/config.ts`, `prepararTelemetria()` utiliza `??=`: pone `false` solo si la variable no existe, **no sobrescribe un `true` configurado por ti**.

---

## Paso 3: generar la primera traza

Prueba primero algo corto:

```powershell
npm run preguntar -- "Hola, ¿qué puedes hacer?"
```

Debería aparecer una traza del agente y su llamada al modelo. No necesita tools si el modelo sigue las instrucciones.

Después prueba una consulta real de laboratorio:

```powershell
npm run preguntar -- "¿Cuántas tareas hay?"
```

Esta sí puede recorrer modelo → obtener_esquema → modelo → consultar_sql → modelo. El orden concreto depende del agente; observa lo que ocurrió, no una secuencia supuesta.

Con OpenAI estos comandos consumen API; con Ollama consumen recursos locales. En ambos casos, al activar tracing se envía telemetría a LangSmith. No hacen una escritura de tarea.

El proyecto ya llama a `awaitAllCallbacks()` al terminar. Eso ayuda a enviar las trazas pendientes antes de cerrar el proceso. No cierres a la fuerza la terminal justo al obtener la respuesta.

---

## Paso 4: ver la ejecución en LangSmith

1. Vuelve al workspace correcto en la web.
2. Abre el agente registrado y su sección **Tracing**, con entorno **Local**; o abre el tracing project si elegiste la ruta B.
3. Busca la ejecución reciente y ábrela.
4. Despliega los pasos: modelo, tools y middleware. Puede haber más nodos que los del esquema conceptual.
5. Mira qué paso tarda más, qué tools se ejecutaron y si hubo errores.

Una **traza** representa la petición completa. Cada llamada o paso dentro de ella es un **run/span**. La duración total no se obtiene siempre sumando duraciones de hijos, porque puede haber anidamiento o paralelismo.

Con `LANGSMITH_HIDE_INPUTS/OUTPUTS=true`, las entradas y salidas aparecerán ocultas. **Es lo esperado, no un fallo.** Para una demostración que requiera ver prompts, SQL y observaciones, utiliza únicamente datos ficticios de un tablero de laboratorio y cambia temporalmente ambos valores a `false`. Ejecuta una nueva petición: el cambio no reconstruye cuerpos que nunca se enviaron.

Volver a ocultarlos no borra lo ya enviado. Si necesitas eliminar trazas anteriores, hazlo mediante las opciones y permisos de LangSmith.

---

## Paso 5: qué enseñar en la sesión

### 5.1 Comparar consola y web

`preguntar` muestra progreso local. LangSmith permite inspeccionar después el árbol de ejecución y sus métricas. Son dos vistas del mismo proceso, no dos agentes diferentes.

### 5.2 Buscar por tags y metadata

En `src/sesion.ts` ya enviamos tags como `delivery-board`, proveedor y modo de skills; metadata como proyecto y proveedor. Úsalos para filtrar o identificar ejecuciones, según los controles disponibles en tu web.

No inventes un dashboard obligatorio para esta primera demo: empieza abriendo dos trazas y comparando duración, llamadas y resultado. Los tags de proveedor se aplican a las invocaciones de la sesión, no necesariamente a scripts aislados.

### 5.3 Tokens, coste y fallos

- Inspecciona tokens cuando el proveedor y la integración los reporten.
- No presentes un coste estimado como una factura exacta ni asumas un precio cloud para Ollama.
- Una traza «correcta» técnicamente no demuestra que la respuesta sea verdadera. Observabilidad no sustituye la evaluación.
- Si aparece el timeout local conocido, comprueba si terminó en el primer modelo o después de una herramienta. No lo atribuyas automáticamente a SQL.

### 5.4 Varias preguntas en una conversación (extra)

```powershell
npm start
```

El mismo `thread_id` conserva contexto en el proceso; `/nueva` abre otro hilo. Si quieres identificar explícitamente los turnos en la web, añade `thread_id: threadId` a la metadata que ya existe en `src/sesion.ts`, sin quitar `configurable.thread_id`. El primero ayuda a agrupar trazas y el segundo identifica el estado del grafo. No confundas un ID de hilo con una credencial ni uses datos personales como ID.

---

## Paso 6: desactivar tracing

Al terminar el laboratorio, cambia en `.env` y reinicia el proceso:

```dotenv
LANGSMITH_TRACING=false
LANGCHAIN_TRACING_V2=false
```

Puedes conservar la clave en tu `.env` privado o retirarla. El agente sigue funcionando; lo que se desactiva es el envío automático de nuevas trazas por esa configuración. No se borran las anteriores.

## Si no aparecen trazas

| Síntoma | Qué revisar |
|---|---|
| Respuesta local, pero nada en la web | Flags, destino, región, workspace, filtros de fecha y variables heredadas |
| Error de autenticación | Clave LangSmith, región y permisos; no la clave OpenAI |
| No encuentra agente | Registrar primero y copiar su ID exacto; no solo el display name |
| Destinos en conflicto | No combinar ID/entorno con variables de proyecto |
| Cuerpos vacíos | HIDE_INPUTS/HIDE_OUTPUTS: puede ser intencional |
| Falta una traza al cerrar | Permitir el flush de callbacks y comprobar conexión a Internet |
| prueba:offline no envía trazas | Ese script las desactiva deliberadamente; usa el comando de la demo |

## Qué queda preparado

No se ha activado nada al crear este documento: los pasos los ejecutas tú. Esta guía no cambia `.env`, claves ni el proveedor, no despliega el agente y no crea una cuenta remota automáticamente.

Continúa con [el guardrail sencillo](GUARDRAIL-SENCILLO.md).

Fuentes: [tracing con LangChain](https://docs.langchain.com/langsmith/trace-with-langchain), [crear el registro del agente](https://docs.langchain.com/langsmith/create-an-agent), [destino agente/entorno](https://docs.langchain.com/langsmith/log-traces-to-agent), [conceptos](https://docs.langchain.com/langsmith/observability-concepts) y [privacidad de trazas](https://docs.langchain.com/langsmith/mask-inputs-outputs). Documentación revisada el 3 de noviembre de 2026; los nombres de botones pueden variar.
