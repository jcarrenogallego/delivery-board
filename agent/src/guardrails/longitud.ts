import { createMiddleware } from "langchain";
import { AIMessage, isHumanMessage } from "@langchain/core/messages";

export const MAX_CARACTERES = 500;

export const guardrailLongitud = createMiddleware({
  name: "LimiteLongitudEntrada",
  beforeAgent: {
    canJumpTo: ["end"],
    hook: (state) => {
      const ultimoUsuario = state.messages.filter(isHumanMessage).at(-1);
      if (!ultimoUsuario || typeof ultimoUsuario.content !== "string") return;

      if (ultimoUsuario.content.length > MAX_CARACTERES) {
        return {
          messages: [new AIMessage(
            `La petición supera ${MAX_CARACTERES} caracteres. Resúmela y vuelve a intentarlo.`,
          )],
          jumpTo: "end",
        };
      }
    },
  },
});
