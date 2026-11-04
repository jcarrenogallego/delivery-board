# Timeout en la consulta local

La configuración observada usa Ollama qwen3:4b, skills inline y REQUEST_TIMEOUT_MS=120000.

## Resultado reproducido

- API y MCP respondían health200.
- Ollama respondía y mostraba qwen3:4b cargado con size_vram=0, contexto activo4096 y soporte declarado completion/tools/thinking.
- La reproducción de «¿Cuántas tareas hay?» agotó120 segundos en **modelo#1**, sin iniciar ninguna herramienta. No se ejecutó SQL ni se modificaron tareas.

Esto localiza la espera en la primera inferencia del modelo. No demuestra por sí solo que el origen sea exclusivamente CPU, tamaño del contexto, configuración o actividad de otros procesos. Tampoco demuestra que el modelo completará correctamente todo el agente si se amplía el tiempo.

## Mejora del diagnóstico

`src/progreso.ts` añade callbacks con nombres y tiempos. `src/sesion.ts` admite callbacks opcionales. `scripts/preguntar.ts` informa proveedor y límite, muestra progreso en vivo y maneja TimeoutError/AbortError sin un stack sin contexto.

Los callbacks no imprimen prompts, respuestas del modelo ni filas SQL. El ejemplo preguntar conserva la impresión de sus observaciones finales: no debe utilizarse para publicar datos sensibles en una consola compartida.

El tiempo límite se cuenta desde que se invoca el agente y cubre todas sus rondas. Una consulta habitual requiere al menos decidir obtener esquema, decidir el SQL y redactar la respuesta. No son120 segundos para cada llamada.

## Siguiente prueba

1. Detén otras generaciones locales que puedan competir por recursos.
2. En .env puedes aumentar temporalmente REQUEST_TIMEOUT_MS a300000.
3. Ejecuta de nuevo `npm run preguntar -- "¿Cuántas tareas hay?"` y observa la última etapa.
4. Si sigue esperando en modelo#1, habrá que medir y ajustar la inferencia/contexto. Si pasa a herramientas y luego vuelve al modelo, ya podremos distinguir si el presupuesto total es insuficiente para varias rondas.

No se ha cambiado .env, modelo, timeout ni skills automáticamente. No se ha llamado a OpenAI. Ampliar tiempo es una prueba diagnóstica, no una solución garantizada. El modo progressive reduce las instrucciones iniciales, pero puede añadir llamadas extra y no se ha validado aquí como solución.

Después de los cambios, TypeScript y las pruebas offline seguían pasando. Las pruebas de preparación originales de VERIFICACION.md son históricas: esta reproducción posterior sí invocó Ollama local.
