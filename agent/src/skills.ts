import { readdir, readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { join } from "node:path";
import { parse } from "yaml";
import { z } from "zod";

const meta = z.object({
  name: z.string().regex(/^[a-z0-9]+(?:-[a-z0-9]+)*$/).max(64),
  description: z.string().min(1).max(1024),
});
export type Skill = { name: string; description: string; instrucciones: string };

// Directorio fijo del proyecto: el modelo no puede leer rutas arbitrarias.
export async function cargarSkills(): Promise<Skill[]> {
  const raiz = fileURLToPath(new URL("../skills/", import.meta.url));
  const carpetas = await readdir(raiz, { withFileTypes: true });
  const skills: Skill[] = [];
  for (const carpeta of carpetas.filter((d) => d.isDirectory()).sort((a, b) => a.name.localeCompare(b.name))) {
    const contenido = await readFile(join(raiz, carpeta.name, "SKILL.md"), "utf8");
    const partes = contenido.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]*)$/);
    if (!partes) throw new Error(`Frontmatter inválido en skill ${carpeta.name}.`);
    const cabecera = meta.parse(parse(partes[1]));
    if (cabecera.name !== carpeta.name) throw new Error("La skill y su carpeta deben tener el mismo nombre.");
    skills.push({ ...cabecera, instrucciones: partes[2].trim() });
  }
  return skills;
}

export function contextoSkills(skills: Skill[], modo: "inline" | "progressive") {
  if (modo === "inline") return skills.map((s) => `## Skill: ${s.name}\n${s.instrucciones}`).join("\n\n");
  return "Skills disponibles. Usa cargar_skill antes de abordar cada especialidad:\n" +
    skills.map((s) => `- ${s.name}: ${s.description}`).join("\n");
}
