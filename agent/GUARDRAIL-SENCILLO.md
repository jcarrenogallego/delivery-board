# Sesión 15: añadir un guardrail sencillo al agente

Vamos a añadir una regla nueva y fácil de demostrar:

> **El agente no procesa mensajes de más de 500 caracteres. Pide resumirlos antes de llamar al modelo o a las herramientas.**

El límite es una decisión didáctica del ejercicio, no una restricción obligatoria de LangGraph. Puedes elegir otro valor después.

Los límites de llamadas, la validación de las tareas y la confirmación antes de crear **ya existen y se mantienen**. No los vamos a quitar para fabricar una demo insegura.

```text
Usuario → guardrail de longitud
              ├── hasta 500 → agente → modelo / herramientas
              └── más de 500 → «Resume la petición» → END
```

| Paso | Qué haces | Qué aprendes |
|---|---|---|
| 0 | Identificar la regla | Guardrail frente a prompt |
| 1 | Crear el middleware | Intervenir antes del agente |
| 2 | Probarlo sin modelo real | Ver aceptado/bloqueado en consola |
| 3 | Conectarlo a Delivery Board | Añadirlo sin perder los controles existentes |
| 4 | Compararlo en LangSmith | Ver que el bloqueo evita la llamada al modelo |

**Solo se entrega esta guía.** Los archivos TypeScript descritos no se han añadido al proyecto todavía. Las pruebas imprimen resultados, sin assert.

---

## Paso 0: entender el control

Un prompt puede decir «no proceses mensajes largos». Este guardrail **lo comprueba en código**, sin pedir al modelo que decida.

- **Middleware:** mecanismo que ejecuta la comprobación.
- **Guardrail:** política «no aceptar más de 500 caracteres».
- **LangSmith:** registra la ejecución para inspeccionarla; no aplica esta regla por sí solo.

Estamos controlando el texto de entrada de la consola. No es un detector de ataques, no evita todos los abusos y no limita el tamaño del historial o de los resultados de herramientas. Tampoco equivale a contar tokens.

Desde la carpeta `projects`:

```powershell
cd delivery-board-agent
```

---

## Paso 1: crear el guardrail

### 1.1 Crea `src/guardrails/longitud.ts`

Primero crea la carpeta `guardrails` dentro de `src`. Después guarda este archivo:

```ts
import { createMiddleware } from "langchain";
import { AIMessage, isHumanMessage } from "@langchain/core/messages";

export const MAX_CARACTERES = 500;

export const guardrailLongitud = createMiddleware({
  name: "LimiteLongitudEntrada",
  beforeAgent: {
    canJumpTo: ["end"],
    hook: (state) => {
      const ultimoUsuario = state.messages.filter(isHumanMessage).at(-1);
      if (!ultimoUsuario || typeof ultimoUsuario.content !== "string") return;

      if (ultimoUsuario.content.length > MAX_CARACTERES) {
        return {
          messages: [new AIMessage(
            `La petición supera ${MAX_CARACTERES} caracteres. Resúmela y vuelve a intentarlo.`,
          )],
          jumpTo: "end",
        };
      }
    },
  },
});
```

### 1.2 Qué hace cada parte

- `beforeAgent`: comprueba una vez al comenzar la ejecución, antes de las llamadas al modelo y tools.
- Busca el **último mensaje del usuario**, no el primero de toda la conversación.
- Si cumple la regla, no devuelve cambios y el agente continúa.
- Si la incumple, devuelve una respuesta controlada y `jumpTo: "end"`.
- `canJumpTo` declara que ese hook puede terminar la ejecución.

No necesita otro LLM. No borra mensajes del historial. El `.length` de JavaScript es una medida sencilla sobre cadenas (un emoji puede ocupar más de una unidad); para esta demo usamos texto normal. La regla se limita a mensajes de texto de la consola, no valida contenido multimodal.

---

## Paso 2: probarlo solo, sin OpenAI, Ollama ni MCP

### 2.1 Crea `scripts/prueba-guardrail.ts`

Usamos un modelo simulado incluido en LangChain. Así comprobamos el guardrail incluso si Ollama tarda demasiado.

```ts
import { createAgent } from "langchain";
import { MemorySaver } from "@langchain/langgraph";
import { FakeListChatModel } from "@langchain/core/utils/testing";
import { awaitAllCallbacks } from "@langchain/core/callbacks/promises";
import { guardrailLongitud } from "../src/guardrails/longitud.ts";

const agente = createAgent({
  name: "demo-guardrail-longitud",
  model: new FakeListChatModel({ responses: ["Petición aceptada por el modelo simulado."] }),
  tools: [],
  middleware: [guardrailLongitud],
  checkpointer: new MemorySaver(),
});

const threadId = `demo-guardrail-${Date.now()}`;

try {
  for (const texto of ["Hola", "a".repeat(500), "a".repeat(501), "Hola otra vez"]) {
    let llamadas = 0;
    const resultado = await agente.invoke({
      messages: [{ role: "user", content: texto }],
    }, {
      configurable: { thread_id: threadId },
      callbacks: [{ handleChatModelStart() { llamadas++; } }],
      tags: ["sesion15", "guardrail-demo"],
      metadata: { thread_id: threadId, longitud: texto.length, modelo: "simulado" },
    });

    console.log("\nCaracteres:", texto.length);
    console.log("Respuesta:", resultado.messages.at(-1)?.content);
    console.log("Llamadas al modelo:", llamadas);
  }
} finally {
  await awaitAllCallbacks();
}
```

### 2.2 Ejecuta

Sin cargar `.env`, para comprobar primero la lógica local (asegúrate de que la terminal no tenga tracing activado):

```powershell
node scripts/prueba-guardrail.ts
```

Salida esperada:

```text
Caracteres: 4
Respuesta: Petición aceptada por el modelo simulado.
Llamadas al modelo: 1

Caracteres: 500
Respuesta: Petición aceptada por el modelo simulado.
Llamadas al modelo: 1

Caracteres: 501
Respuesta: La petición supera 500 caracteres. Resúmela y vuelve a intentarlo.
Llamadas al modelo: 0

Caracteres: 13
Respuesta: Petición aceptada por el modelo simulado.
Llamadas al modelo: 1
```

El mensaje bloqueado no impide aceptar uno corto posterior en ese mismo hilo. Tampoco se bloquea el turno de 500 solo porque el historial acumulado sea mayor: comprobamos la entrada actual.

El texto rechazado permanece en el historial: esta regla no lo elimina ni garantiza que no vuelva a aparecer como contexto en un turno posterior. Para comparar casos aislados en la consola real, usa `/nueva` después de un bloqueo.

Este script no usa proveedor real, MCP ni API. Si activas tracing, sí podrá enviar sus trazas a LangSmith, con texto ficticio; no significa que haya llamado a OpenAI.

---

## Paso 3: conectarlo al agente de Delivery Board

### 3.1 Actualiza `src/agente.ts`

Conserva los límites existentes y añade el guardrail al principio de la lista. El archivo queda así:

```ts
import { createAgent, modelCallLimitMiddleware, toolCallLimitMiddleware } from "langchain";
import { MemorySaver } from "@langchain/langgraph";
import type { BaseChatModel } from "@langchain/core/language_models/chat_models";
import type { Config } from "./config.ts";
import type { Skill } from "./skills.ts";
import type { crearHerramientas } from "./herramientas.ts";
import { crearPrompt } from "./prompt.ts";
import { guardrailLongitud } from "./guardrails/longitud.ts";

export function crearAgente(config: Config, modelo: BaseChatModel, tools: ReturnType<typeof crearHerramientas>, skills: Skill[]) {
  return createAgent({
    name: "delivery-board-agent", model: modelo, tools,
    systemPrompt: crearPrompt(config, skills),
    checkpointer: new MemorySaver(),
    middleware: [
      guardrailLongitud,
      modelCallLimitMiddleware({ runLimit: config.maxModelCalls, exitBehavior: "error" }),
      toolCallLimitMiddleware({ toolName: "consultar_sql", runLimit: config.maxSqlAttempts, exitBehavior: "continue" }),
      toolCallLimitMiddleware({ toolName: "crear_tarea", runLimit: 1, exitBehavior: "continue" }),
    ],
  });
}
export type Agente = ReturnType<typeof crearAgente>;
```

No necesitas cambiar las skills ni pedirle al modelo que aplique la regla. El control lo ejecuta el middleware.

### 3.2 Comprueba y ejecuta

```powershell
npm run check
npm start
```

En la consola prueba:

```text
Hola, ¿qué puedes hacer?
```

Después pega un texto de 501 caracteres. Para preparar uno, puedes imprimirlo en otra terminal:

```powershell
node -e "console.log('a'.repeat(501))"
```

Copia únicamente la línea generada, no el comando. El agente debe responder con el mensaje de bloqueo sin invocar al modelo ni a las tools.

**Límite de la demo:** el proceso crea el runtime y conecta con MCP antes de recibir la petición. Por eso la aplicación completa sigue necesitando su configuración y MCP disponibles; el guardrail evita llamadas de inferencia y de herramientas para ese texto, no el arranque de la aplicación.

---

## Paso 4: verlo en LangSmith

Configura primero [la guía de observabilidad](OBSERVABILIDAD-LANGSMITH.md). Con tracing habilitado, puedes generar las cuatro trazas sintéticas sin depender del modelo real:

```powershell
node --env-file-if-exists=.env scripts/prueba-guardrail.ts
```

En LangSmith busca el tag `guardrail-demo` y compara:

| Petición | Qué debes ver |
|---|---|
| Corta o de 500 caracteres | Hook del middleware y una llamada al modelo simulado |
| De 501 caracteres | Hook del middleware y salida controlada, sin llamada al modelo |

El nodo puede mostrarse con un nombre similar a `LimiteLongitudEntrada.before_agent`, según la versión. Abre el árbol y comprueba los pasos, no solo el estado general.

**El bloqueo no tiene por qué aparecer como error rojo:** hemos terminado normalmente con un mensaje de rechazo, no lanzado una excepción. La evidencia es que no hay una llamada al modelo después del hook. Si ocultaste inputs/outputs, el árbol seguirá siendo útil; para ver el texto ficticio aplica la opción de visibilidad de la primera guía.

En el agente real, prueba los mismos dos tipos de mensaje con `npm start`: verás el guardrail junto a los middleware que ya teníamos.

Para comparar «sin» y «con» el nuevo guardrail, comenta temporalmente **solo** la línea `guardrailLongitud,` y utiliza una entrada ficticia. No retires límites de llamadas, confirmación de creación ni controles del MCP/API. El script simulado es la alternativa sin consumo de proveedor.

---

## Qué hemos demostrado y qué no

- La regla es determinista y distinta de los límites actuales: valida el tamaño de la entrada, no el número de llamadas.
- Un guardrail puede cortar el flujo antes del modelo sin usar IA para tomar esa decisión.
- LangSmith permite observar el corte; no es quien lo aplica.
- El guardrail no oculta datos de telemetría. El mensaje original puede formar parte de la traza raíz si no se oculta la entrada; no uses esta regla como filtro de privacidad.
- No comprueba permisos, SQL, inyección de prompts o calidad de respuestas. Es una demostración pequeña, no un sistema completo de seguridad.
- Comprueba peticiones representadas como `HumanMessage`, no automáticamente el texto de una reanudación con `Command({ resume: ... })`. Validar también esas aclaraciones sería una ampliación posterior.

No hemos implementado automáticamente ninguno de estos archivos en tu aplicación al crear el documento. Los fragmentos se han comprobado aparte con las dependencias actuales y un modelo simulado; no se ha activado LangSmith ni llamado a modelos reales para verificarlos.

Fuente: [guardrails y middleware de LangChain](https://docs.langchain.com/oss/javascript/langchain/guardrails). Revisado el 3 de noviembre de 2026.
