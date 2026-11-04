import { leerConfig } from "../src/config.ts";
const config = leerConfig();
console.table(config);
console.log("Clave OpenAI:", process.env.OPENAI_API_KEY?.trim() ? "configurada (no se muestra)" : "pendiente");
console.log("Sin conexiones a modelos ni servicios.");
