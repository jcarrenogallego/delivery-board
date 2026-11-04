import { leerConfig } from "../src/config.ts";
import { conectarMcp } from "../src/integraciones/mcp.ts";
const mcp = await conectarMcp(leerConfig().mcpUrl);
try {
  console.log("Herramientas MCP:", mcp.nombres);
  console.log("Esquema real:\n", await mcp.esquema());
  console.log("SELECT 1:\n", await mcp.consultar('SELECT 1 AS "Conexion"'));
} finally { await mcp.close(); }
console.log("Prueba de lectura; sin modelo y sin escritura de tareas.");
