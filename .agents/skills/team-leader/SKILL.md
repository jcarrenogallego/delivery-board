---
name: team-leader
description: Invoca al agente Team Leader para convertir una historia de usuario en diseño técnico y tareas, asignar trabajo y evaluar bloqueantes, progreso y cumplimiento.
---

# Invocar team-leader

Usa el rol definido en `.codex/agents/team-leader.toml` como fuente de sus responsabilidades; esta skill es una entrada de delegación.

- Si la invocación no incluye una tarea ni contexto suficiente, pide únicamente el requerimiento o la ruta del entregable necesario. No inventes trabajo.
- Delega con `spawn_agent`, usando exactamente `agent_type: "team-leader"`. Transmite la petición del usuario, el contexto y las rutas relevantes, el alcance autorizado y sus restricciones. El agente debe consultar su definición TOML y las instrucciones aplicables del proyecto.
- Espera el resultado del agente y presenta sus entregables, comprobaciones y pendientes con evidencia. Si requiere una definición del usuario, eleva su pregunta y continúa solo cuando la respuesta permita hacerlo; no inventes respuestas ni autorizaciones.
- No vuelvas a invocar esta skill desde el agente delegado. Si faltan las herramientas de delegación o el rol no está disponible, informa la limitación sin simular que ejecutaste el agente ni sustituirlo silenciosamente.
