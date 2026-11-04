import { leerConfig } from "../src/config.ts";
const config = leerConfig();
for (const [nombre, url] of [["API", `${config.apiUrl}/health`], ["MCP", new URL("/health", config.mcpUrl).href]]) {
  try {
    const response = await fetch(url, { signal: AbortSignal.timeout(5000) });
    console.log(nombre, url, "→ HTTP", response.status, await response.text());
    if (!response.ok) process.exitCode = 1;
  } catch { console.log(nombre, url, "→ no disponible; comprueba proceso y puerto."); process.exitCode = 1; }
}
console.log("Solo health checks. No se ha llamado a ningún modelo ni modificado datos.");
