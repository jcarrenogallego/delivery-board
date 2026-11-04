import { randomUUID } from "node:crypto";
import { Command } from "@langchain/langgraph";
import type { Agente } from "./agente.ts";
import type { Config } from "./config.ts";
import type { RunnableConfig } from "@langchain/core/runnables";

export function crearSesion(agente: Agente, config: Config) {
  let threadId = randomUUID();
  let pendiente = false;
  return {
    nueva() { threadId = randomUUID(); pendiente = false; },
    async enviar(texto: string, callbacks?: RunnableConfig["callbacks"]) {
      const entrada = pendiente
        ? new Command({ resume: texto })
        : { messages: [{ role: "user" as const, content: texto }] };
      const resultado = await agente.invoke(entrada, {
        configurable: { thread_id: threadId }, recursionLimit: config.recursionLimit,
        signal: AbortSignal.timeout(config.timeoutMs),
        callbacks,
        tags: ["delivery-board", config.provider, `skills-${config.skillsMode}`],
        metadata: { proyecto: "delivery-board-agent", provider: config.provider, skills_mode: config.skillsMode },
      });
      const pausas = (resultado as { __interrupt__?: { value: unknown }[] }).__interrupt__ ?? [];
      pendiente = pausas.length > 0;
      return { resultado, pausas, threadId };
    },
  };
}
