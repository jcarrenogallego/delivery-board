import { awaitAllCallbacks } from "@langchain/core/callbacks/promises";
import { crearRuntime } from "../src/runtime.ts";
import { crearSesion } from "../src/sesion.ts";
import { crearProgreso } from "../src/progreso.ts";
import { isAIMessage, isToolMessage } from "@langchain/core/messages";

async function main() {
  const pregunta = process.argv.slice(2).join(" ").trim();
  if (!pregunta) throw new Error('Ejemplo: npm run preguntar -- "¿Cuántas tareas pendientes hay?"');
  const runtime = await crearRuntime();
  const progreso = crearProgreso();
  console.log(`Proveedor: ${runtime.config.provider}. Límite total: ${runtime.config.timeoutMs / 1000}s.`);
  try {
    const { resultado, pausas } = await crearSesion(runtime.agente, runtime.config).enviar(pregunta, progreso.callbacks);
    if (pausas.length) console.log("Se requiere interacción. Usa npm start para hacer el flujo completo:", pausas);
    else {
      for (const mensaje of resultado.messages) {
        if (isAIMessage(mensaje) && mensaje.tool_calls?.length) console.log("Solicita:", mensaje.tool_calls);
        if (isToolMessage(mensaje)) console.log("Observación:", mensaje.content);
      }
      console.log("Respuesta:", resultado.messages.at(-1)?.content);
    }
  } catch (error) {
    if (error instanceof Error && ["TimeoutError", "AbortError"].includes(error.name)) {
      console.error(`Se agotó el límite total de ${runtime.config.timeoutMs / 1000}s. Última etapa: ${progreso.ultimaEtapa()}.`);
      console.error("Comprueba los tiempos anteriores. REQUEST_TIMEOUT_MS se puede ajustar en .env; aumentarlo no corrige bucles ni errores del modelo.");
      process.exitCode = 1;
    } else throw error;
  } finally { await runtime.close(); await awaitAllCallbacks(); }
}

main().catch((error) => {
  console.error("No se pudo completar:", error instanceof Error ? error.message : "Error inesperado.");
  process.exitCode = 1;
});
