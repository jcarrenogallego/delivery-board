import * as readline from "node:readline/promises";
import { stdin, stdout } from "node:process";
import { awaitAllCallbacks } from "@langchain/core/callbacks/promises";
import { crearRuntime } from "./runtime.ts";
import { crearSesion } from "./sesion.ts";

async function main() {
  const runtime = await crearRuntime();
  const sesion = crearSesion(runtime.agente, runtime.config);
  const rl = readline.createInterface({ input: stdin, output: stdout });
  console.log(`Delivery Board · ${runtime.config.provider} · skills ${runtime.config.skillsMode}`);
  console.log("Consulta tareas o pide crear una. /nueva reinicia la conversación; salir termina.\n");
  try {
    while (true) {
      const texto = (await rl.question("Tú: ")).trim();
      if (!texto) continue;
      if (texto.toLowerCase() === "salir") break;
      if (texto === "/nueva") { sesion.nueva(); console.log("Conversación nueva."); continue; }
      try {
        const { resultado, pausas } = await sesion.enviar(texto);
        if (pausas.length) {
          for (const pausa of pausas) console.log("Asistente necesita datos:", JSON.stringify(pausa.value, null, 2));
        } else console.log("Asistente:", resultado.messages.at(-1)?.content, "\n");
      } catch (error) {
        console.error("No se completó:", error instanceof Error ? error.message : "Error inesperado.");
        console.log("Si hubo una creación, consulta el tablero antes de repetir. Usa /nueva si el hilo quedó incompleto.");
      }
    }
  } finally {
    rl.close(); await runtime.close(); await awaitAllCallbacks();
  }
}
main().catch((error) => { console.error(error instanceof Error ? error.message : "Error al iniciar."); process.exitCode = 1; });
