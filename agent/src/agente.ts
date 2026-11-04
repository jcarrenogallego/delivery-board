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
      modelCallLimitMiddleware({ runLimit: config.maxModelCalls, exitBehavior: "error" }),
      toolCallLimitMiddleware({ toolName: "consultar_sql", runLimit: config.maxSqlAttempts, exitBehavior: "continue" }),
      // Una intención de creación por turno. No reintentar automáticamente una escritura.
      toolCallLimitMiddleware({ toolName: "crear_tarea", runLimit: 1, exitBehavior: "continue" }),
      guardrailLongitud,
    ],
  });
}
export type Agente = ReturnType<typeof crearAgente>;
