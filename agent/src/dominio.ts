import { z } from "zod";

export const NuevaTarea = z.object({
  title: z.string().trim().min(1).max(160).describe("Título indicado por el usuario"),
  owner: z.string().trim().min(1).max(80).describe("Responsable indicado por el usuario"),
  priority: z.enum(["Low", "Medium", "High"]).default("Medium"),
});
export type NuevaTarea = z.infer<typeof NuevaTarea>;

export type Columna = { name: string; type?: string };
export type Filas = {
  columns: Columna[]; rows: unknown[][]; rowCount: number;
  truncated?: boolean; truncationReason?: string | null;
};

export function formatearFilas(filas: Filas) {
  const lineas = filas.rows.map((row) =>
    row.map((valor, i) => `${filas.columns[i]?.name ?? i}: ${JSON.stringify(valor)}`).join(" | "),
  );
  return {
    rowCount: filas.rowCount, truncated: filas.truncated ?? false,
    truncationReason: filas.truncationReason ?? null,
    datos: lineas.length ? lineas.join("\n") : "Sin resultados.",
  };
}

export function resumirEsquema(bruto: unknown) {
  const esquema = z.object({ schemas: z.array(z.object({
    name: z.string(), tables: z.array(z.object({
      name: z.string(), columns: z.array(z.object({ name: z.string(), type: z.string() })),
    })),
  })) }).parse(bruto);
  const tablas = esquema.schemas.flatMap((s) => s.tables
    .filter((t) => t.name === "work_items")
    .map((t) => `${JSON.stringify(s.name)}.${JSON.stringify(t.name)}(${t.columns
      .map((c) => `${JSON.stringify(c.name)} ${c.type}`).join(", ")})`));
  if (!tablas.length) throw new Error("El MCP no devolvió la tabla work_items; revisa base de datos y migraciones.");
  return tablas.join("\n");
}
