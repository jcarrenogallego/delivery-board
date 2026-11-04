import { randomUUID } from "node:crypto";
import { awaitAllCallbacks } from "@langchain/core/callbacks/promises";
import { crearRuntime } from "../src/runtime.ts";
const runtime = await crearRuntime();
try {
  const stream = await runtime.agente.stream({
    messages: [{ role: "user", content: process.argv.slice(2).join(" ") || "¿Cuántas tareas pendientes hay?" }],
  }, {
    configurable: { thread_id: randomUUID() }, streamMode: "updates",
    recursionLimit: runtime.config.recursionLimit,
    signal: AbortSignal.timeout(runtime.config.timeoutMs),
  });
  for await (const paso of stream) console.dir(paso, { depth: 6 });
  console.log("Para continuar una aclaración o confirmación usa la consola interactiva, npm start.");
} finally { await runtime.close(); await awaitAllCallbacks(); }
