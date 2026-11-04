import { leerConfig, prepararTelemetria } from "./config.ts";
import { crearModelo } from "./modelo.ts";
import { cargarSkills } from "./skills.ts";
import { conectarMcp } from "./integraciones/mcp.ts";
import { crearClienteApi } from "./integraciones/api.ts";
import { crearHerramientas } from "./herramientas.ts";
import { crearAgente } from "./agente.ts";

export async function crearRuntime() {
  prepararTelemetria();
  const config = leerConfig();
  const modelo = crearModelo(config); // Valida clave sin hacer llamadas.
  const skills = await cargarSkills();
  const mcp = await conectarMcp(config.mcpUrl);
  try {
    const tools = crearHerramientas(mcp, crearClienteApi(config.apiUrl), skills);
    return { agente: crearAgente(config, modelo, tools, skills), config, close: mcp.close };
  } catch (error) { await mcp.close(); throw error; }
}
