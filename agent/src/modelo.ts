import { ChatOpenAI } from "@langchain/openai";
import { ChatOllama } from "@langchain/ollama";
import type { Config } from "./config.ts";

export function crearModelo(config: Config) {
  if (config.provider === "ollama") {
    return new ChatOllama({
      model: config.ollamaModel, baseUrl: config.ollamaUrl,
      temperature: 0, maxRetries: 0,
      // Evita imponer think:false a modelos antiguos que no lo admiten.
      ...(config.ollamaModel.startsWith("qwen3") ? { think: false } : {}),
    });
  }
  const apiKey = process.env.OPENAI_API_KEY?.trim();
  if (!apiKey) throw new Error("Falta OPENAI_API_KEY en .env. No se ha llamado a OpenAI.");
  return new ChatOpenAI({
    model: config.openaiModel, apiKey, temperature: 0,
    maxRetries: 0, timeout: config.timeoutMs,
  });
}
