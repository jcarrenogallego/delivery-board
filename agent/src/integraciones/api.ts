import { NuevaTarea } from "../dominio.ts";

export function crearClienteApi(apiUrl: string, timeoutMs = 15_000, http: typeof fetch = fetch) {
  return {
    async crear(datos: NuevaTarea, signal?: AbortSignal) {
      const tarea = NuevaTarea.parse(datos);
      let response: Response;
      try {
        response = await http(`${apiUrl}/api/work-items`, {
          method: "POST", headers: { "Content-Type": "application/json" },
          body: JSON.stringify(tarea),
          signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(timeoutMs)]) : AbortSignal.timeout(timeoutMs),
        });
      } catch {
        // Un timeout de escritura no demuestra que la API no haya creado la tarea.
        return { ok: false, estadoDesconocido: true,
          error: "No se pudo confirmar la creación. Consulta el tablero antes de reintentar; podría haberse creado." };
      }
      if (!response.ok) {
        return { ok: false, error: `La API rechazó crear la tarea (HTTP ${response.status}). Revisa datos y disponibilidad.` };
      }
      try {
        const creada = await response.json() as Record<string, unknown>;
        if (typeof creada.id !== "string" || typeof creada.title !== "string") throw new Error();
        return { ok: true, tarea: creada };
      } catch {
        return { ok: false, estadoDesconocido: true,
          error: "La API aceptó la solicitud, pero no se pudo leer la confirmación. No repitas automáticamente." };
      }
    },
  };
}
export type ClienteApi = ReturnType<typeof crearClienteApi>;
