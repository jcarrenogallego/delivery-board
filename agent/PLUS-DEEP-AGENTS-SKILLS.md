# Plus · Delivery Board con Deep Agents y skills nativas

## ¿Qué vamos a cambiar?

Vamos a conservar nuestro agente original y añadir una variante con **Deep Agents**, una librería del ecosistema LangChain construida sobre LangGraph.

La diferencia que nos interesa es este atributo:

```ts
skills: ["/"]
```

En nuestra configuración, `/` es la raíz virtual del backend, que apuntará únicamente a la carpeta `skills/`. **No es la raíz del ordenador.**

Hasta ahora cargábamos los Markdown con `cargarSkills()` y los añadíamos al prompt o los leíamos con `cargar_skill`. En esta variante, la librería descubre las skills y proporciona el mecanismo para leerlas cuando se necesitan.

> No es añadir `skills` al `createAgent` original: cambiaremos a `createDeepAgent`, de `deepagents`. Las skills aportan instrucciones, no herramientas nuevas ni garantías de acierto del modelo.

Este extra contiene solo los cambios para usar la variante: sin pruebas ni nuevos ejercicios.

---

## Paso 1. Instalar la librería

Abre la terminal en la raíz de `delivery-board-agent` y ejecuta:

```bash
npm install --save-exact deepagents@1.16.2
```

La guía utiliza esta versión para que los nombres y opciones no dependan de futuras actualizaciones. Se actualizarán `package.json` y `package-lock.json`. No desinstales LangChain ni LangGraph: siguen formando parte de la solución. No uses `--force` ni `--legacy-peer-deps`.

Seguimos reutilizando `crearModelo()`: la selección de OpenAI u Ollama y sus variables de entorno no cambian. Para este extra puedes mantener el OpenAI que ya tienes configurado. Añadir skills no garantiza que un modelo local pequeño maneje bien las herramientas adicionales.

## Paso 2. Reutilizar nuestras skills

La estructura que ya tenemos sirve:

```text
skills/
  delivery-consultas/SKILL.md
  delivery-creacion/SKILL.md
  delivery-react/SKILL.md
```

Cada `SKILL.md` conserva su cabecera YAML con `name` y `description`, seguida de las instrucciones. Deep Agents utiliza esa descripción para presentar las capacidades disponibles y puede leer el contenido completo mediante `read_file`.

Solo hay una instrucción que debemos adaptar para no pedir una herramienta que ya no usaremos. En `skills/delivery-react/SKILL.md`, sustituye esta línea:

```text
- En modo progressive, usa cargar_skill con delivery-consultas o delivery-creacion para leer sus reglas. En modo inline ya están en el mensaje de sistema.
```

Por esta:

```text
- Si las reglas de otra skill no están en tu contexto, léelas con el mecanismo disponible: cargar_skill en la variante original; read_file sobre la ruta de su SKILL.md en Deep Agents. Si ya están en el mensaje de sistema, no necesitas volver a cargarlas.
```

Así el mismo fichero sigue sirviendo para las dos variantes.

## Paso 3. Crear `src/agente-deep.ts`

No borres `src/agente.ts`. Crea este fichero nuevo:

```ts
import { fileURLToPath } from "node:url";
import { createDeepAgent, FilesystemBackend } from "deepagents";
import { MemorySaver } from "@langchain/langgraph";
import { modelCallLimitMiddleware, toolCallLimitMiddleware } from "langchain";
import type { BaseChatModel } from "@langchain/core/language_models/chat_models";
import type { Config } from "./config.ts";
import type { crearHerramientas } from "./herramientas.ts";
import { crearPrompt } from "./prompt.ts";

export async function crearAgenteDeep(
  config: Config,
  modelo: BaseChatModel,
  tools: ReturnType<typeof crearHerramientas>,
) {
  // Solo exponemos skills/, nunca la raíz del proyecto ni su .env.
  const backend = new FilesystemBackend({
    rootDir: fileURLToPath(new URL("../skills/", import.meta.url)),
    virtualMode: true,
  });

  return await createDeepAgent({
    name: "delivery-board-agent-deep",
    model: modelo,
    tools: tools.filter((t) => t.name !== "cargar_skill"),
    // Conservamos las reglas de negocio, sin inyectar las skills a mano.
    systemPrompt: crearPrompt({ ...config, skillsMode: "inline" }, []) + `
Las skills las gestiona Deep Agents. Lee sus SKILL.md mediante read_file.
Lee /delivery-react/SKILL.md antes de resolver la petición.
Para consultas, lee /delivery-consultas/SKILL.md.
Para creaciones, lee /delivery-creacion/SKILL.md.
No uses cargar_skill: en esta variante no está disponible.
No delegues con task: este ejercicio trabaja con un solo agente.`,
    backend,
    skills: ["/"],
    permissions: [
      { operations: ["write"], paths: ["/**"], mode: "deny" },
    ],
    checkpointer: new MemorySaver(),
    middleware: [
      modelCallLimitMiddleware({ runLimit: config.maxModelCalls, exitBehavior: "error" }),
      toolCallLimitMiddleware({ toolName: "consultar_sql", runLimit: config.maxSqlAttempts, exitBehavior: "continue" }),
      toolCallLimitMiddleware({ toolName: "crear_tarea", runLimit: 1, exitBehavior: "continue" }),
      // Impide delegar y saltarse los límites de las herramientas del agente principal.
      toolCallLimitMiddleware({ toolName: "task", runLimit: 0, exitBehavior: "continue" }),
    ],
  });
}

export type AgenteDeep = Awaited<ReturnType<typeof crearAgenteDeep>>;

```

### ¿Qué cambia aquí?

- `createDeepAgent` sustituye a `createAgent` en esta variante.
- `skills` declara dónde descubrir las skills. Sus rutas son virtuales y usan `/`, también en Windows.
- El backend está limitado a `skills/`, no al proyecto completo; así no exponemos `.env` a sus herramientas de archivos.
- `permissions` impide modificar esos ficheros mediante las herramientas de archivos integradas. No sustituye los permisos del MCP o de la API.
- Conservamos el prompt de negocio sin insertar manualmente las instrucciones de las skills. `SKILLS_MODE` ya no decide cómo se cargan en esta variante.
- Eliminamos `cargar_skill` de las herramientas expuestas: su lugar lo ocupa la lectura nativa.

**Deep Agents añade más que skills:** incluye herramientas de archivos, planificación y delegación. Por eso no veremos exactamente el mismo grafo. En este extra bloqueamos `task` con un límite de cero para trabajar con un solo agente y evitar que la delegación eluda nuestros límites de consultas y creación. No añadimos un backend de ejecución de comandos.

Se conservan los límites existentes y la confirmación humana dentro de `crear_tarea`. La regla de no repetir una escritura sigue siendo importante.

> Si ya implementaste el guardrail de longitud de la otra guía, copia también su import y añade `guardrailLongitud` al principio de este array `middleware`. No se hereda automáticamente desde `agente.ts`.

## Paso 4. Crear `src/runtime-deep.ts`

El `index.ts` actual no construye directamente el agente: llama a `crearRuntime()`. Por eso necesitamos esta variante del runtime, sin modificar el original.

```ts
import { leerConfig, prepararTelemetria } from "./config.ts";
import { crearModelo } from "./modelo.ts";
import { conectarMcp } from "./integraciones/mcp.ts";
import { crearClienteApi } from "./integraciones/api.ts";
import { crearHerramientas } from "./herramientas.ts";
import { crearAgenteDeep } from "./agente-deep.ts";

export async function crearRuntime() {
  prepararTelemetria();
  const config = leerConfig();
  const modelo = crearModelo(config);
  const mcp = await conectarMcp(config.mcpUrl);
  try {
    // Ya no cargamos las skills manualmente. Deep Agents las lee del backend.
    const tools = crearHerramientas(mcp, crearClienteApi(config.apiUrl), []);
    const agente = await crearAgenteDeep(config, modelo, tools);
    return { agente, config, close: mcp.close };
  } catch (error) {
    await mcp.close();
    throw error;
  }
}

```

Seguimos conectándonos al mismo MCP y a la misma API. Pasamos `[]` a `crearHerramientas` porque ya no necesitamos el catálogo manual; después se excluye `cargar_skill`. Las herramientas de consultar, pedir aclaraciones y crear tareas siguen siendo las mismas.

## Paso 5. Crear `src/sesion-deep.ts`

Para conservar también los tipos y la sesión original, copia `src/sesion.ts` a `src/sesion-deep.ts`. Puedes hacerlo desde el editor o con PowerShell:

```powershell
Copy-Item src/sesion.ts src/sesion-deep.ts
```

En **la copia**, cambia únicamente este import:

```ts
import type { Agente } from "./agente.ts";
```

Por este:

```ts
import type { AgenteDeep as Agente } from "./agente-deep.ts";
```

El resto se queda igual: mensajes, `thread_id`, reanudación con `Command`, pausas de confirmación y límites de tiempo. El alias `as Agente` evita tener que renombrar el parámetro de la función.

## Paso 6. Seleccionar la variante desde `src/index.ts`

En `src/index.ts`, sustituye estos dos imports:

```ts
import { crearRuntime } from "./runtime.ts";
import { crearSesion } from "./sesion.ts";
```

Por estos:

```ts
import { crearRuntime } from "./runtime-deep.ts";
import { crearSesion } from "./sesion-deep.ts";
```

**No cambies el resto del `index.ts`.** Las funciones conservan los mismos nombres y la misma dinámica de uso.

El recorrido queda así:

```text
index.ts
  → runtime-deep.ts
      → agente-deep.ts
          → createDeepAgent({ skills, backend, tools, ... })
  → sesion-deep.ts
```

## Paso 7. Arrancar la variante

Detén el proceso anterior si seguía abierto y arranca normalmente:

```bash
npm start
```

No necesitas otro servidor para Deep Agents. Mantén accesibles el MCP y la API de Delivery Board, como antes. La configuración de modelo y de telemetría sigue viniendo del `.env` existente.

Este cambio selecciona la variante en el chat de `npm start`. **Los scripts que importan el runtime original siguen usando el agente original.**

Si también quieres que `npm run preguntar` utilice Deep Agents, en `scripts/preguntar.ts` cambia los mismos dos imports:

```ts
import { crearRuntime } from "../src/runtime-deep.ts";
import { crearSesion } from "../src/sesion-deep.ts";
```

El script de dibujo `scripts/dibujar.ts` seguirá mostrando el grafo original mientras importe `crearAgente` desde `agente.ts`; la imagen de la sesión anterior corresponde a esa variante, no a Deep Agents.

## Volver a la versión anterior

En `index.ts`, recupera estos imports:

```ts
import { crearRuntime } from "./runtime.ts";
import { crearSesion } from "./sesion.ts";
```

Si cambiaste `scripts/preguntar.ts`, restaura allí también los imports originales. No necesitas borrar los ficheros nuevos para volver al agente anterior.

---

## Resumen

| Antes | Variante del plus |
| --- | --- |
| `createAgent` | `createDeepAgent` |
| Skills mediante nuestro cargador/prompt/herramienta | Skills declaradas en `skills` y leídas mediante el backend |
| `runtime.ts` y `sesion.ts` | `runtime-deep.ts` y `sesion-deep.ts` |
| MCP, API y confirmación humana | Los mismos, reutilizados |

**El cambio visible en el arranque son dos imports; los ficheros nuevos encapsulan la variante sin eliminar la anterior.**

### Referencias

- [Skills de Deep Agents](https://docs.langchain.com/oss/javascript/deepagents/skills).
- [Personalización y modelos](https://docs.langchain.com/oss/javascript/deepagents/customization).
- [Permisos del sistema de archivos](https://docs.langchain.com/oss/javascript/deepagents/permissions).
- [Código y API de Deep Agents JS](https://github.com/langchain-ai/deepagentsjs).
