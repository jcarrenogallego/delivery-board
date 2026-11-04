import { cargarSkills, contextoSkills } from "../src/skills.ts";
const skills = await cargarSkills();
console.table(skills.map(({ name, description, instrucciones }) => ({ name, description, caracteres: instrucciones.length })));
console.log("Catálogo progressive:\n", contextoSkills(skills, "progressive"));
console.log("Sin LLM; las skills se han leído desde archivos del proyecto.");
