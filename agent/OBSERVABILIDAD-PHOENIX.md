# Observabilidad local · Delivery Board Agent con Phoenix

## ¿Qué vamos a hacer?

Conectar nuestro agente con **Arize Phoenix**, una plataforma de observabilidad que podemos ejecutar en local.

Mantendremos el modelo, el grafo, las herramientas MCP y la API. Añadiremos la instrumentación que recoge las ejecuciones y las envía a Phoenix:

```text
Delivery Board Agent
        ↓
Instrumentación de LangChain con OpenInference
        ↓
OpenTelemetry → Phoenix local → interfaz web
```

**Este documento es solo una guía.** Los cambios se harán cuando sigas los pasos; no hay que ejecutar ningún script de pruebas ni crear otro agente.

> Phoenix local no convierte el modelo en local: si el proveedor es OpenAI, las peticiones al modelo siguen yendo a OpenAI. Lo que alojamos aquí es la observabilidad.

---

## Paso 1. Arrancar Phoenix

Necesitas tener `uv` disponible en el equipo. Si todavía no lo tienes, sigue su [instalación oficial](https://docs.astral.sh/uv/getting-started/installation/).

Abre una terminal y ejecuta:

```bash
uvx arize-phoenix serve
```

Deja esa terminal abierta y accede a [http://localhost:6006](http://localhost:6006).

Este comando inicia Phoenix; **todavía no recibe las trazas de nuestro agente**. A continuación conectaremos la aplicación.

## Paso 2. Instalar los paquetes en el proyecto

En otra terminal, sitúate en la raíz de `delivery-board-agent` y ejecuta:

```bash
npm install @arizeai/phoenix-otel @arizeai/openinference-instrumentation-langchain@^4
```

- `phoenix-otel` configura el envío de trazas.
- La instrumentación de LangChain recoge sus ejecuciones.

La versión 4 de la instrumentación admite `@langchain/core` 1.x, que es la familia que utiliza nuestro proyecto. No hace falta instalar la variante `langchain-v0`.

Este paso actualizará `package.json` y `package-lock.json`. No desinstales LangChain ni LangGraph.

## Paso 3. Configurar privacidad y desactivar el envío a LangSmith

En `.env`, establece estos valores **sustituyendo los que existan**, sin duplicarlos:

```dotenv
# En este ejercicio enviamos las trazas a Phoenix, no a LangSmith.
LANGSMITH_TRACING=false
LANGCHAIN_TRACING_V2=false

# Ocultar entradas y salidas en las trazas de OpenInference.
OPENINFERENCE_HIDE_INPUTS=true
OPENINFERENCE_HIDE_OUTPUTS=true
```

No borres ni cambies la clave de OpenAI, el proveedor, el modelo o las direcciones del MCP y la API.

Las variables de privacidad de LangSmith **no controlan Phoenix**: por eso añadimos las de `OPENINFERENCE`.

Con los valores anteriores empezaremos viendo el recorrido y sus tiempos, pero sin el contenido completo de entradas y salidas. Para inspeccionarlo después, usa únicamente datos ficticios de laboratorio, cambia ambas variables a `false` y reinicia el agente. El cambio no recupera contenidos que no se registraron previamente.

> Ocultar entradas y salidas no es una garantía de que ningún otro atributo o error contenga información sensible. No uses secretos ni datos personales en las peticiones del ejercicio. Tampoco publiques el servidor local en Internet.

## Paso 4. Crear `src/telemetria-phoenix.ts`

Crea este fichero nuevo:

```ts
import { register } from "@arizeai/phoenix-otel";
import { LangChainInstrumentation } from
  "@arizeai/openinference-instrumentation-langchain";
import * as CallbackManagerModule from
  "@langchain/core/callbacks/manager";

export const phoenix = register({
  projectName: "delivery-board-agent",
  url: "http://localhost:6006",
  batch: true,
});

const instrumentacion = new LangChainInstrumentation({
  tracerProvider: phoenix,
});

instrumentacion.manuallyInstrument(CallbackManagerModule);
```

Nuestro proyecto utiliza módulos ESM (`"type": "module"`). Por eso instrumentamos explícitamente el gestor de callbacks de LangChain.

`url` es la dirección base de Phoenix; en esta opción no tenemos que añadir `/v1/traces`. El ejemplo supone Phoenix local sin autenticación; una instancia protegida necesitará su configuración de acceso.

No copies esta inicialización al runtime ni al agente: queremos un único proveedor de telemetría por proceso.

## Paso 5. Cargar la telemetría antes que la aplicación

Dentro de `scripts` en `package.json`, sustituye únicamente `start` y `preguntar` por estas entradas:

```json
"start": "node --env-file-if-exists=.env --import ./src/telemetria-phoenix.ts src/index.ts",
"preguntar": "node --env-file-if-exists=.env --import ./src/telemetria-phoenix.ts scripts/preguntar.ts"
```

Mantén las demás entradas y las comas necesarias del JSON.

`--import` carga la instrumentación antes de ejecutar el fichero principal. El `.env` se carga desde el arranque, de modo que las opciones de privacidad están disponibles al inicializarla.

Esto no cambia qué variante del agente seleccionaste en `index.ts`: si elegiste la variante Deep Agents de la guía del plus, conserva esos imports.

## Paso 6. Enviar las trazas pendientes al cerrar el chat

En `src/index.ts`, añade este import junto a los que ya tienes:

```ts
import { phoenix } from "./telemetria-phoenix.ts";
```

El módulo ya cargado por `--import` se reutiliza; este import permite acceder al proveedor para cerrarlo.

Sustituye el `finally` exterior que cierra el chat por este:

```ts
  } finally {
    rl.close();
    try {
      await runtime.close();
      await awaitAllCallbacks();
    } finally {
      await phoenix.shutdown();
    }
  }
```

Conserva todo lo demás: lectura de mensajes, sesión, manejo de errores y llamada a `main()`.

`shutdown()` espera el envío pendiente y cierra el proveedor. Se ejecuta al terminar la aplicación, **no después de cada mensaje**, porque entonces dejaríamos de registrar los siguientes turnos.

## Paso 7. Aplicar el mismo cierre al comando `preguntar`

En `scripts/preguntar.ts`, añade:

```ts
import { phoenix } from "../src/telemetria-phoenix.ts";
```

Sustituye el `finally` actual:

```ts
  } finally { await runtime.close(); await awaitAllCallbacks(); }
```

Por este:

```ts
  } finally {
    try {
      await runtime.close();
      await awaitAllCallbacks();
    } finally {
      await phoenix.shutdown();
    }
  }
```

No cambies el resto del script. Este cierre es especialmente útil porque el comando termina tras una sola petición y las trazas se exportan en lotes.

## Paso 8. Usar el agente y abrir sus trazas

Mantén arrancados Phoenix, el MCP y los servicios de Delivery Board. Reinicia el agente:

```bash
npm start
```

Puedes continuar la conversación habitual, por ejemplo:

```text
¿Cuántas tareas hay?
```

Abre [Phoenix](http://localhost:6006) y busca el proyecto **`delivery-board-agent`**. Podrás inspeccionar las ejecuciones registradas, llamadas al modelo, herramientas, tiempos y errores cuando se produzcan. El recorrido exacto depende de las decisiones del agente; no todas las peticiones usan todas las herramientas.

Para cerrar el chat de forma normal, escribe `salir`. Así se ejecuta el cierre de la telemetría. Una finalización forzada puede perder datos pendientes.

También puedes usar el comando de una sola pregunta:

```bash
npm run preguntar -- "¿Cuántas tareas hay?"
```

Si no ves trazas, revisa que Phoenix siga abierto, que hayas reiniciado el agente con los scripts actualizados y que estés mirando el proyecto correcto. Con `batch: true` puede haber un pequeño retraso de exportación.

---

## ¿Qué no necesitamos cambiar?

- La construcción del grafo en `agente.ts`.
- Las herramientas y su conexión MCP.
- La API y las confirmaciones de creación.
- El modelo seleccionado y su clave.
- Los guardrails existentes.

Esta instrumentación observa las llamadas de LangChain; no instala telemetría dentro de los procesos independientes del MCP o de la API. Ver una llamada a una herramienta no equivale a disponer de trazas internas de PostgreSQL o del backend .NET.

## Volver al arranque anterior

Restaura los dos scripts:

```json
"start": "node --env-file-if-exists=.env src/index.ts",
"preguntar": "node --env-file-if-exists=.env scripts/preguntar.ts"
```

Retira también los imports de `phoenix` y sus llamadas a `shutdown()` en `index.ts` y `preguntar.ts`; dejar esos imports seguiría inicializando Phoenix aunque quitaras `--import`.

Si quieres volver a LangSmith, recupera los valores de tracing que tenías siguiendo [su guía](OBSERVABILIDAD-LANGSMITH.md). No hace falta borrar la configuración de OpenAI ni las claves para cambiar de observabilidad.

## Referencias

- [Phoenix: arranque local](https://github.com/Arize-ai/phoenix#run-locally).
- [Integración de OpenInference para LangChain.js](https://github.com/Arize-ai/openinference/tree/main/js/packages/openinference-instrumentation-langchain).
- [SDK Phoenix OpenTelemetry y opciones de privacidad](https://arize-ai.github.io/phoenix/modules/_arizeai_phoenix-otel.html).
