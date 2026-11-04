import { tool } from "@langchain/core/tools";
import { z } from "zod";
import { leerConfig, prepararTelemetria } from "../src/config.ts";
import { crearModelo } from "../src/modelo.ts";
prepararTelemetria();
const config = leerConfig();
// Prueba inocua: no conecta al MCP ni puede escribir tareas.
const tools = [tool(() => "Esquema de prueba", {
  name: "obtener_esquema", description: "Obtiene el esquema para consultar tareas.", schema: z.object({}),
})];
const respuesta = await crearModelo(config).bindTools(tools).invoke([
  { role: "system", content: "Para consultar tareas debes llamar a obtener_esquema antes de responder." },
  { role: "user", content: "¿Cuántas tareas tiene Ana? Utiliza obtener_esquema primero." },
], { signal: AbortSignal.timeout(config.timeoutMs) });
console.log("Texto:", respuesta.content);
console.log("tool_calls:", respuesta.tool_calls);
console.log("invalid_tool_calls:", respuesta.invalid_tool_calls);
if (!respuesta.tool_calls?.some((t) => t.name === "obtener_esquema")) {
  console.log("El modelo no utilizó tool calling correctamente. Más skills no garantizan resolverlo.");
  process.exitCode = 1;
}
