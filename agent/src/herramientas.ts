import { tool } from "@langchain/core/tools";
import { interrupt } from "@langchain/langgraph";
import { z } from "zod";
import { NuevaTarea, formatearFilas } from "./dominio.ts";
import type { ConexionMcp } from "./integraciones/mcp.ts";
import type { ClienteApi } from "./integraciones/api.ts";
import type { Skill } from "./skills.ts";

export function crearHerramientas(mcp: ConexionMcp, api: ClienteApi, skills: Skill[]) {
  const obtenerEsquema = tool(async (_, config) => {
    try { return JSON.stringify({ ok: true, esquema: await mcp.esquema(config) }); }
    catch (error) { return JSON.stringify({ ok: false, error: String(error) }); }
  }, {
    name: "obtener_esquema", description: "Lee por MCP tabla y columnas reales de work_items. Antes de escribir SQL.",
    schema: z.object({}),
  });

  const consultarSql = tool(async ({ sql }, config) => {
    try { return JSON.stringify({ ok: true, ...formatearFilas(await mcp.consultar(sql, config)) }); }
    catch (error) { return JSON.stringify({ ok: false, error: String(error), accion: "Corrige SQL usando el esquema real y el error; no inventes filas." }); }
  }, {
    name: "consultar_sql", description: "Ejecuta una consulta PostgreSQL de solo lectura por MCP. Devuelve filas o error para corregir el SQL.",
    schema: z.object({ sql: z.string().min(1).describe("Una SELECT, identificadores PascalCase entre comillas dobles, LIMIT 50 salvo agregaciones") }),
  });

  const aclaracion = tool(async ({ pregunta }) => {
    const respuesta = interrupt({ tipo: "aclaracion", pregunta });
    return JSON.stringify({ respuestaUsuario: String(respuesta) });
  }, {
    name: "pedir_aclaracion", description: "Pausa para preguntar al usuario cuando faltan datos para crear una tarea.",
    schema: z.object({ pregunta: z.string().min(1) }),
  });

  const crearTarea = tool(async (datos, config) => {
    const tarea = NuevaTarea.parse(datos);
    // La pausa sucede ANTES del efecto externo. Al reanudar se reejecuta el nodo.
    const decision = interrupt({ tipo: "confirmacion", pregunta: "¿Crear esta tarea? Responde sí o no.", tarea });
    if (!/^(si|sí|s|yes)$/i.test(String(decision).trim())) {
      return JSON.stringify({ ok: false, cancelado: true, mensaje: "El usuario no confirmó. No se ha creado nada." });
    }
    return JSON.stringify(await api.crear(tarea, config.signal));
  }, {
    name: "crear_tarea", description: "Crea por API una tarea con título y responsable indicados por el usuario; pausa para confirmación antes de escribir.",
    schema: NuevaTarea,
  });

  const cargarSkill = tool(async ({ nombre }) => {
    const skill = skills.find((s) => s.name === nombre);
    return skill ? skill.instrucciones : "Skill no disponible. Consulta el catálogo del sistema.";
  }, {
    name: "cargar_skill", description: "Lee instrucciones de una skill del catálogo del proyecto. No ejecuta scripts ni lee archivos arbitrarios.",
    schema: z.object({ nombre: z.string().describe("Nombre exacto de una skill del catálogo") }),
  });
  return [obtenerEsquema, consultarSql, aclaracion, crearTarea, cargarSkill];
}
