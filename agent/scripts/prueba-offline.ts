// Infraestructura real createAgent + tools del proyecto, pero modelo/MCP/API simulados.
// No requiere .env, no realiza peticiones HTTP y no modifica Delivery Board.
process.env.LANGSMITH_TRACING = "false";
process.env.LANGCHAIN_TRACING_V2 = "false";

import { BaseChatModel } from "@langchain/core/language_models/chat_models";
import { AIMessage, isToolMessage } from "@langchain/core/messages";
import type { BaseMessage } from "@langchain/core/messages";
import type { ChatResult } from "@langchain/core/outputs";
import { leerConfig } from "../src/config.ts";
import { cargarSkills, contextoSkills } from "../src/skills.ts";
import { leerResultadoMcp } from "../src/integraciones/mcp.ts";
import { crearClienteApi } from "../src/integraciones/api.ts";
import { crearHerramientas } from "../src/herramientas.ts";
import { crearAgente } from "../src/agente.ts";
import { crearSesion } from "../src/sesion.ts";
import type { ConexionMcp } from "../src/integraciones/mcp.ts";

function ver(nombre: string, correcto: boolean) {
  console.log(`${correcto ? "OK" : "FALLO"} · ${nombre}`);
  if (!correcto) process.exitCode = 1;
}

class ModeloGuion extends BaseChatModel {
  respuestas: AIMessage[];
  entradas: BaseMessage[][] = [];
  constructor(respuestas: AIMessage[]) { super({}); this.respuestas = [...respuestas]; }
  _llmType() { return "modelo-simulado-sin-red"; }
  bindTools(_tools: unknown[]) { return this; }
  async _generate(messages: BaseMessage[]): Promise<ChatResult> {
    this.entradas.push(messages);
    const message = this.respuestas.shift();
    if (!message) throw new Error("Se agotó el guion simulado.");
    return { generations: [{ text: String(message.content), message }] };
  }
}

let id = 0;
function llamada(name: string, args: Record<string, unknown>) {
  return new AIMessage({ content: "", tool_calls: [{ name, args, id: `offline-${++id}`, type: "tool_call" }] });
}

const config = leerConfig({ MODEL_PROVIDER: "ollama" });
const skills = await cargarSkills();
ver("Tres skills válidas con SKILL.md", skills.length === 3);
ver("Skills inline sin depender de llamada adicional", contextoSkills(skills, "inline").includes("count(*)"));
ver("Catálogo progressive", contextoSkills(skills, "progressive").includes("cargar_skill"));
const dato = leerResultadoMcp([{ type: "text", text: '{"rows":[],"columns":[],"rowCount":0}' }]) as { rowCount: number };
ver("Parseo bloques MCP", dato.rowCount === 0);
let rechazado = false;
try { leerResultadoMcp('{"error":{"code":"database_error"}}'); } catch { rechazado = true; }
ver("Error MCP no se considera resultado vacío", rechazado);

let consultas = 0;
let escrituras = 0;
const mcp: ConexionMcp = {
  nombres: ["get_database_schema", "execute_read_query"], close: async () => {},
  esquema: async () => 'public.work_items("Title" text, "Owner" text)',
  consultar: async () => {
    consultas++;
    if (consultas === 1) throw new Error('Falta entrecomillar "Owner"');
    return { columns: [{ name: "Total" }], rows: [[2]], rowCount: 1, truncated: false };
  },
};
const httpSimulado: typeof fetch = async (_url, options) => {
  escrituras++;
  return Response.json({ id: "fixture-offline", ...JSON.parse(String(options?.body)) }, { status: 201 });
};
const api = crearClienteApi("http://no-se-utiliza", 1000, httpSimulado);
const tools = crearHerramientas(mcp, api, skills);

console.log("\nConsulta con reintento SQL:");
const modeloConsulta = new ModeloGuion([
  llamada("obtener_esquema", {}),
  llamada("consultar_sql", { sql: "SELECT count(*) FROM work_items WHERE Owner = 'Ana'" }),
  llamada("consultar_sql", { sql: 'SELECT count(*) AS "Total" FROM work_items WHERE "Owner" ILIKE \'ana\'' }),
  new AIMessage("Ana tiene 2 tareas."),
  new AIMessage("Puedo consultar y crear tareas."),
]);
const agenteConsulta = crearAgente(config, modeloConsulta, tools, skills);
const sesionConsulta = crearSesion(agenteConsulta, config);
const consulta = await sesionConsulta.enviar("¿Cuántas tareas tiene Ana?");
console.log(consulta.resultado.messages.at(-1)?.content);
ver("Dos consultas y ninguna escritura", consultas === 2 && escrituras === 0);
ver("Error observado antes de la corrección", consulta.resultado.messages.filter(isToolMessage).some((m) => String(m.content).includes("ok\":false")));
await sesionConsulta.enviar("¿Qué puedes hacer?");
ver("Historial entre turnos", modeloConsulta.entradas.at(-1)!.length > 2);

console.log("\nLímite de SQL:");
consultas = 0;
const mcpFallido: ConexionMcp = { ...mcp, consultar: async () => { consultas++; throw new Error("SQL inválido simulado"); } };
const modeloLimite = new ModeloGuion([
  llamada("consultar_sql", { sql: "SELECT 1" }), llamada("consultar_sql", { sql: "SELECT 2" }),
  llamada("consultar_sql", { sql: "SELECT 3" }), new AIMessage("No pude consultar; reformula la pregunta."),
]);
await crearSesion(crearAgente(config, modeloLimite, crearHerramientas(mcpFallido, api, skills), skills), config).enviar("Consulta de prueba");
ver("La tercera consulta se bloquea antes de MCP", consultas === 2);

console.log("\nSkills bajo demanda:");
const modeloSkills = new ModeloGuion([
  llamada("cargar_skill", { nombre: "delivery-consultas" }),
  llamada("obtener_esquema", {}),
  llamada("consultar_sql", { sql: 'SELECT count(*) AS "Total" FROM work_items' }),
  new AIMessage("Hay 2 tareas."),
]);
const configProgresiva = { ...config, skillsMode: "progressive" as const };
const progresivo = await crearSesion(crearAgente(configProgresiva, modeloSkills, tools, skills), configProgresiva).enviar("Cuenta tareas");
ver("El agente carga una skill y luego consulta", progresivo.resultado.messages.filter(isToolMessage).some((m) =>
  m.name === "cargar_skill" && String(m.content).includes("count(*)")));

const modeloLimiteModelo = new ModeloGuion([llamada("obtener_esquema", {}), new AIMessage("No debe llegar aquí")]);
const configLimiteModelo = { ...config, maxModelCalls: 1 };
let limiteModelo = false;
try {
  await crearSesion(crearAgente(configLimiteModelo, modeloLimiteModelo, tools, skills), configLimiteModelo).enviar("Limita el modelo");
} catch { limiteModelo = modeloLimiteModelo.entradas.length === 1; }
ver("El límite corta la segunda llamada al modelo", limiteModelo);

console.log("\nAclaración y confirmación humana:");
const modeloCreacion = new ModeloGuion([
  llamada("pedir_aclaracion", { pregunta: "¿Quién será el responsable?" }),
  llamada("crear_tarea", { title: "Revisar despliegue", owner: "Ana", priority: "High" }),
  new AIMessage("Tarea creada para Ana."),
]);
const sesionCreacion = crearSesion(crearAgente(config, modeloCreacion, tools, skills), config);
const falta = await sesionCreacion.enviar("Crea revisar despliegue con prioridad alta");
console.log("Pausa:", falta.pausas[0]?.value);
ver("Faltan datos: no se escribió", falta.pausas.length === 1 && escrituras === 0);
const confirmar = await sesionCreacion.enviar("Ana");
console.log("Pausa:", confirmar.pausas[0]?.value);
ver("Antes de confirmar: cero escrituras", confirmar.pausas.length === 1 && escrituras === 0);
const creada = await sesionCreacion.enviar("sí");
console.log(creada.resultado.messages.at(-1)?.content);
ver("Reanudación crea una vez, no duplica por interrupt", escrituras === 1 && creada.pausas.length === 0);

const cancelarModelo = new ModeloGuion([
  llamada("crear_tarea", { title: "No crear", owner: "Ana", priority: "Medium" }), new AIMessage("Cancelado."),
]);
const sesionCancelar = crearSesion(crearAgente(config, cancelarModelo, tools, skills), config);
await sesionCancelar.enviar("Crea no crear para Ana");
await sesionCancelar.enviar("no");
ver("Cancelación respeta decisión y no escribe", escrituras === 1);

const cargar = tools.find((t) => t.name === "cargar_skill")!;
ver("Skill solo del catálogo", String(await cargar.invoke({ nombre: "../../.env" })).includes("no disponible"));
const apiTimeout = crearClienteApi("http://no-se-utiliza", 1000, async () => { throw new Error("timeout simulado"); });
const incierto = await apiTimeout.crear({ title: "Prueba", owner: "Ana", priority: "Medium" });
ver("Timeout de creación se marca incierto, no reintentable automáticamente", "estadoDesconocido" in incierto && incierto.estadoDesconocido === true);
console.log("\nFin offline. Modelo, MCP y API simulados. Cero llamadas a OpenAI/Ollama/Delivery Board.");
