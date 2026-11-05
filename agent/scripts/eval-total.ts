import { evaluate } from "langsmith/evaluation";
import { awaitAllCallbacks } from "@langchain/core/callbacks/promises";
import { crearRuntime } from "../src/runtime.ts";
import { crearSesion } from "../src/sesion.ts";

type Datos = Record<string, unknown>;
const pregunta = "¿Cuántas tareas tiene el responsable EvalSesion? Responde únicamente con el número entero, sin texto adicional.";
const notas: number[] = [];
const runtime = await crearRuntime();

// 1. Ejecutar el agente para el ejemplo del dataset.
async function ejecutarAgente(inputs: Datos) {
  if (inputs.pregunta !== pregunta) throw new Error("Pregunta inesperada en el dataset");
  const sesion = crearSesion(runtime.agente, runtime.config);
  const { resultado } = await sesion.enviar(pregunta);
  const respuesta = String(resultado.messages.at(-1)?.content ?? "").trim();
  return { respuesta, total: Number(respuesta) };
}

// 2. La comparación que antes hacíamos directamente en consola.
function evaluarTotal({ outputs, referenceOutputs }: {
  outputs?: Datos;
  referenceOutputs?: Datos;
}) {
  const correcto = outputs?.total === 3 && referenceOutputs?.total === 3;
  const nota = correcto ? 1 : 0;
  notas.push(nota);
  console.log("Total obtenido:", outputs?.total, "Puntuación:", nota);
  return { key: "total_correcto", score: nota };
}

// 3. LangSmith ejecuta el ejemplo y guarda el experimento y su puntuación.
try {
  await evaluate(ejecutarAgente, {
    data: "delivery-board-total-3",
    evaluators: [evaluarTotal],
    experimentPrefix: "delivery-board-total",
    maxConcurrency: 1,
    numRepetitions: 1,
  });

  const aprobado = notas.length === 1 && notas[0] === 1;
  console.log(aprobado ? "Eval aprobado" : "Eval NO aprobado");
  if (!aprobado) process.exitCode = 1;
} finally {
  try { await runtime.close(); }
  finally { await awaitAllCallbacks(); }
}
