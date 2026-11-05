import { z } from "zod";

const entero = (defecto: number) => z.coerce.number().int().positive().default(defecto);
const schema = z.object({
  MODEL_PROVIDER: z.enum(["openai", "ollama"]).default("openai"),
  OPENAI_MODEL: z.string().min(1).default("gpt-4o-mini"),
  OLLAMA_MODEL: z.string().min(1).default("qwen3:4b"),
  OLLAMA_BASE_URL: z.url().default("http://127.0.0.1:11434"),
  DELIVERY_API_URL: z.url().default("http://localhost:5202"),
  MCP_URL: z.url().default("http://127.0.0.1:5091/mcp"),
  SKILLS_MODE: z.enum(["inline", "progressive"]).default("inline"),
  MAX_SQL_ATTEMPTS: entero(2), MAX_MODEL_CALLS: entero(12),
  RECURSION_LIMIT: entero(40), REQUEST_TIMEOUT_MS: entero(120_000),
});

export function leerConfig(env: NodeJS.ProcessEnv = process.env) {
  const datos = schema.parse(env);
  return {
    provider: datos.MODEL_PROVIDER, openaiModel: datos.OPENAI_MODEL,
    ollamaModel: datos.OLLAMA_MODEL, ollamaUrl: datos.OLLAMA_BASE_URL,
    apiUrl: datos.DELIVERY_API_URL.replace(/\/$/, ""), mcpUrl: datos.MCP_URL,
    skillsMode: datos.SKILLS_MODE, maxSqlAttempts: datos.MAX_SQL_ATTEMPTS,
    maxModelCalls: datos.MAX_MODEL_CALLS, recursionLimit: datos.RECURSION_LIMIT,
    timeoutMs: datos.REQUEST_TIMEOUT_MS,
  };
}

export type Config = ReturnType<typeof leerConfig>;

// Las ejecuciones locales no activan telemetría por defecto.
export function prepararTelemetria() {
  process.env.LANGSMITH_TRACING ??= "true";
  process.env.LANGCHAIN_TRACING_V2 ??= "true";
}
