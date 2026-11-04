import { ChatOpenAI } from "@langchain/openai";
import { leerConfig } from "../src/config.ts";
import { cargarSkills } from "../src/skills.ts";
import { crearHerramientas } from "../src/herramientas.ts";
import { crearAgente } from "../src/agente.ts";

// Solo describe el plano. No invoca el modelo ni abre conexiones.
const noEjecutar = async (): Promise<never> => { throw new Error("Esta integración no se ejecuta al dibujar."); };
const skills = await cargarSkills();
const tools = crearHerramientas({ esquema: noEjecutar, consultar: noEjecutar, close: async () => {}, nombres: [] }, { crear: noEjecutar }, skills);
const agente = crearAgente(leerConfig(), new ChatOpenAI({ model: "gpt-4o-mini", apiKey: "no-real-key-not-used" }), tools, skills);
console.log(await agente.drawMermaid());
