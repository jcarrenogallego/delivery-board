import { leerConfig, prepararTelemetria } from "../src/config.ts";
import { crearModelo } from "../src/modelo.ts";
prepararTelemetria();
const config = leerConfig();
const modelo = crearModelo(config);
console.log(`Esta prueba llama realmente a ${config.provider}.`);
const respuesta = await modelo.invoke("Responde solo OK", { signal: AbortSignal.timeout(config.timeoutMs) });
console.log(respuesta.content);
