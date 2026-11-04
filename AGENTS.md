# Reglas del repositorio

## Trabajo directo y herramientas
- Trabaja con las instrucciones del usuario y las convenciones existentes.
- BMAD, OpenSpec y Gentle AI no son requisitos ni pasos obligatorios.
- No instales, actives ni ejecutes esos frameworks, sus skills, workflows o revisiones automáticamente. Este proyecto se mantiene sin sus integraciones locales; cualquier cambio futuro requiere una petición explícita.
- No uses ODD ni crees artefactos de seguimiento bajo odd/.
- Estas reglas se aplican a agentes y subagentes.

## Seguridad y calidad
- Conserva el trabajo existente y los datos ajenos. No descartes cambios ni reescribas el historial Git.
- No uses instalaciones globales, credenciales o servicios remotos sin autorización explícita para el destino y la operación.
- No hagas push, PR ni merge sin petición del usuario.
- Mantén las reglas funcionales de docs/functional-contract.md; no amplíes el alcance sin aprobación.
- Ejecuta las pruebas aplicables y comunica resultados observados, fallos y comprobaciones pendientes; no declares una prueba ejecutada por leer evidencia antigua.
- Usa PostgreSQL aislado para verificaciones destructivas; elimina exclusivamente fixtures propias. No borres volúmenes ni contenedores ajenos.
- Conserva hooks Git y validaciones de compilación/pruebas. Ninguna herramienta de IA es necesaria para arrancar o verificar la aplicación.

## Skills del proyecto
- `historias-usuario`: para crear o refinar historias con contexto, alcance, reglas de negocio y criterios de aceptación en Gherkin, lee `.agents/skills/historias-usuario/SKILL.md`. Guarda las historias como `.md` dentro del proyecto; por defecto en `docs/historias-usuario/`.
- `product-owner`: entrada `$product-owner` para delegar en el agente del mismo nombre; lee `.agents/skills/product-owner/SKILL.md`.
- `team-leader`: entrada `$team-leader` para delegar en el agente del mismo nombre; lee `.agents/skills/team-leader/SKILL.md`.
- `developer-backend`: entrada `$developer-backend` para delegar en el agente del mismo nombre; lee `.agents/skills/developer-backend/SKILL.md`.
- `developer-front`: entrada `$developer-front` para delegar en el agente del mismo nombre; lee `.agents/skills/developer-front/SKILL.md`.

## Agentes del proyecto
- `developer-front`: definido en `.codex/agents/developer-front.toml`. Úsalo cuando se solicite al Developer Front extraer o implementar las tareas frontend del entregable del Team Leader, construir interfaces, optimizar rendimiento o verificar flujos y responsive hasta tablet en el navegador embebido de Codex. Ejemplo: «Usa el agente developer-front para implementar y verificar las tareas frontend de este diseño técnico».
- `developer-backend`: definido en `.codex/agents/developer-backend.toml`. Úsalo cuando se solicite al Developer Backend extraer o implementar las tareas backend del entregable del Team Leader, incluyendo lógica de negocio, seguridad, excepciones, interoperabilidad y pruebas unitarias. Ejemplo: «Usa el agente developer-backend para implementar las tareas backend de este diseño técnico».
- `team-leader`: definido en `.codex/agents/team-leader.toml`. Úsalo cuando se solicite trabajar con el Team Leader para diseñar la solución de una historia, desglosar y asignar tareas, resolver bloqueantes o evaluar progreso y cumplimiento técnico. Guarda el diseño y las tareas en un `.md` del proyecto; por defecto en `docs/disenos-tecnicos/`. Ejemplo: «Usa el agente team-leader para diseñar la solución técnica de esta historia de usuario».
- `product-owner`: definido en `.codex/agents/product-owner.toml`. Úsalo cuando se solicite trabajar con el Product Owner para estructurar requerimientos, crear historias con `historias-usuario`, decidir aprobaciones funcionales, priorizar o validar objetivos. Ejemplo: «Usa el agente product-owner para convertir este requerimiento en historias de usuario».
