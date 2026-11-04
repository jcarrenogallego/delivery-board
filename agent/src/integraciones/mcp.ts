import { MCPAdapter } from "@langchain/mcp-adapters";
import type { RunnableConfig } from "@langchain/core/runnables";
import { z } from "zod";
import { resumirEsquema } from "../dominio.ts";
import type { Filas } from "../dominio.ts";

// Adaptamos el contenido MCP sin confundir {error:...} con datos válidos.
export function leerResultadoMcp(salida: unknown): unknown {
  if (typeof salida === "string") {
    try { return leerResultadoMcp(JSON.parse(salida)); }
    catch (error) {
      if (error instanceof SyntaxError) throw new Error("Respuesta MCP no es JSON válido.");
      throw error;
    }
  }
  if (!salida || typeof salida !== "object") throw new Error("Respuesta MCP vacía o inválida.");
  const valor = salida as Record<string, unknown>;
  if (valor.error) throw new Error(`MCP: ${JSON.stringify(valor.error)}`);
  if (valor.isError || valor.status === "error") throw new Error(`MCP rechazó la operación: ${JSON.stringify(valor.content)}`);
  if (valor.structuredContent) return leerResultadoMcp(valor.structuredContent);
  if (Array.isArray(salida)) {
    const textos = salida.filter((b) => b?.type === "text" && typeof b.text === "string");
    if (textos.length === 1) return leerResultadoMcp(textos[0].text);
  }
  // Dentro del agente, el contexto tool_call puede envolver el resultado en
  // un ToolMessage: content puede ser JSON en texto, no solo bloques MCP.
  if (typeof valor.content === "string" || Array.isArray(valor.content)) {
    return leerResultadoMcp(valor.content);
  }
  return valor;
}

export type ConexionMcp = {
  esquema: (config?: RunnableConfig) => Promise<string>;
  consultar: (sql: string, config?: RunnableConfig) => Promise<Filas>;
  close: () => Promise<void>;
  nombres: string[];
};

export async function conectarMcp(url: string): Promise<ConexionMcp> {
  const adapter = new MCPAdapter({
    servers: { deliveryboard: { url } }, prefixToolNameWithServerName: false,
  });
  try {
    const tools = await adapter.listTools();
    const esquema = tools.find((t) => t.name === "get_database_schema");
    const consulta = tools.find((t) => t.name === "execute_read_query");
    if (!esquema || !consulta) throw new Error("Faltan get_database_schema o execute_read_query en el MCP.");
    return {
      nombres: tools.map((t) => t.name), close: () => adapter.close(),
      esquema: async (config) => resumirEsquema(leerResultadoMcp(await esquema.invoke({}, config))),
      consultar: async (sql, config) => {
        const dato = leerResultadoMcp(await consulta.invoke({ sql }, config));
        return z.object({
          columns: z.array(z.object({ name: z.string(), type: z.string().optional() })),
          rows: z.array(z.array(z.unknown())), rowCount: z.number(),
          truncated: z.boolean().optional(), truncationReason: z.string().nullable().optional(),
        }).parse(dato);
      },
    };
  } catch (error) {
    await adapter.close();
    throw error;
  }
}
