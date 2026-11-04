# Base preparada para sesión15

Tema previsto: **Observabilidad, Telemetría Avanzada y Guardrails con LangSmith**.

Esta sesión trabajará con Delivery Board Agent, no con la calculadora. Primero hay que validar una consulta y una creación real cuando el usuario autorice OpenAI y estén arrancados API, MCP y PostgreSQL.

## Ya disponible

- createAgent con bucle modelo/tools y memoria por hilo.
- Herramientas identificadas por nombre: obtener_esquema, consultar_sql, pedir_aclaracion, crear_tarea y cargar_skill.
- Tags del proveedor y modo de skills; metadata de proyecto en las invocaciones de consola.
- Límites básicos de modelo, SQL y una creación por turno; confirmación humana y validaciones de argumentos.
- Ejemplos de consola para comparar la misma pregunta con OpenAI u Ollama, inline o progressive.

## Todavía no activado

- No hay clave LangSmith ni trazas enviadas por defecto.
- No se han creado proyectos/datasets/evaluadores en LangSmith ni hecho pruebas pagadas OpenAI.
- No se ha implementado un paquete de guardrails avanzados. Los controles mínimos anteriores no se retirarán para fabricar una demo insegura.

## Demostración propuesta (por preparar después)

1. Activar tracing explícitamente con una clave guardada localmente, revisar qué datos se enviarán y usar datos de laboratorio, no secretos ni datos personales reales.
2. Ver en la web el árbol modelo→esquema→consulta→modelo, duración, errores y tokens cuando el proveedor los reporte. No asumir coste en euros para modelos locales.
3. Comparar una consulta correcta y otra con error; observar el reintento.
4. Ver pausa de datos faltantes y confirmación de creación.
5. Comparar skills inline/progressive y proveedores sin confundir más contexto con más fiabilidad.
6. Introducir guardrails adicionales, evaluar su resultado y explicar que evaluar a posteriori no deshace una acción.

Antes de activar tracing, decidir redacción/exclusión de entradas, resultados SQL y datos del tablero. No basta con ocultar la clave en console.log para evitar filtraciones en telemetría.
