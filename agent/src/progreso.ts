import type { RunnableConfig } from "@langchain/core/runnables";

// Solo nombres y tiempos: no registra prompts, claves ni filas del tablero.
export function crearProgreso() {
  const inicio = Date.now();
  const activos = new Map<string, { nombre: string; inicio: number }>();
  let llamadasModelo = 0;
  let ultima = "inicio";
  const imprimir = (texto: string) => console.log(`[${((Date.now() - inicio) / 1000).toFixed(1)}s] ${texto}`);
  function empezar(id: string, nombre: string) {
    activos.set(id, { nombre, inicio: Date.now() }); ultima = nombre;
    imprimir(`Inicio: ${nombre}`);
  }
  function terminar(id: string, fallo = false) {
    const paso = activos.get(id);
    if (!paso) return;
    imprimir(`${fallo ? "Error" : "Fin"}: ${paso.nombre} (${((Date.now() - paso.inicio) / 1000).toFixed(1)}s)`);
    activos.delete(id);
  }
  const callbacks: RunnableConfig["callbacks"] = [{
    name: "progreso-consola",
    handleChatModelStart(_modelo: unknown, _messages: unknown, id: string) {
      empezar(id, `modelo #${++llamadasModelo}`);
    },
    handleLLMEnd(_output: unknown, id: string) { terminar(id); },
    handleLLMError(_error: unknown, id: string) { terminar(id, true); },
    handleToolStart(_tool: unknown, _input: string, id: string, _parent?: string,
      _tags?: string[], _metadata?: Record<string, unknown>, nombre?: string) {
      empezar(id, `herramienta ${nombre ?? "sin nombre"}`);
    },
    handleToolEnd(_output: unknown, id: string) { terminar(id); },
    handleToolError(_error: unknown, id: string) { terminar(id, true); },
  }];
  return { callbacks, ultimaEtapa: () => ultima };
}
