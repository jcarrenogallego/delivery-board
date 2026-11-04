# Evidencia histórica: no acredita el estado actual

Esta comprobación corresponde a la versión del 14-10-2026 anterior a filtros y dependencias. Debe repetirse tras los cambios actuales; no implica aprobación ni ejecución de una revisión automática.

# Verificación de navegador — Configurar prioridad

2026-10-14. Interfaz real mediante cua_repl, http://localhost:13000, API 15202 y PostgreSQL aislado del plan; no se modificaron bases de otros proyectos. Observaciones manuales, no una suite frontend ficticia.

- Crear «Prueba de prioridad» con Media inicial: tarjeta y métricas visibles. Otra tarjeta no creada por esta prueba se conservó intacta.
- Pending: seleccionar Baja, Media y Alta; confirmación sin navegación; recarga mostró Alta persistida.
- Abrir editor de responsable, escribir «Borrador conservado» y guardar Baja: texto del borrador y responsable Ana conservados, foco en selector; cancelar borrador sin guardar.
- Iniciar: pasó a En curso; seleccionar cada nivel y recargar conservó Alta. Total 2, Pendientes 1, En curso 1, Completadas 0 durante ediciones de prioridad.
- Completar: pasó a Completada; selector editable en Baja/Media/Alta y recarga con Alta. No aparecieron Cambiar responsable, Iniciar o Completar en la tarjeta completada. Total 2, Pendientes 1, En curso 0, Completadas 1 no cambiaron al editar prioridad.
- Teclado: ArrowUp cambió Alta a Media en Completed y confirmó; foco volvió al selector. A 390 × 844, scrollWidth 375 ≤ innerWidth 390; a escritorio 1120 × 900, scrollWidth 1105 ≤ 1120. Se restauró el viewport original.
- Fallos controlados CDP, exclusivamente para la tarea creada: 400 restauró Baja y mostró «La prioridad no es válida. No se guardó el cambio.»; 200 con cuerpo incompleto y fallo de red restauraron Baja y mostraron incertidumbre, sin falso éxito; Actualizar consultó la vista real. Son fallos simulados de transporte/respuesta; los 400/404 reales se probaron por HTTP.
- Lectura obsoleta: detener GET dashboard con prioridad Media; guardar Alta y completar su lectura nueva; entregar después el dashboard anterior con Media. La tarjeta conservó Alta antes y después de esa respuesta antigua. Se retiraron todas las intercepciones con Fetch.enable patterns=[] y se dejó red normal.
- Captura: browser-priority.jpg muestra selector y métricas en la composición heredada, incluida una completada editable; no acredita por sí sola persistencia.
- No hay filtros de vista o dependencias en esta versión: CA-10/19 condicionales no aplicables; no se implementaron filtros, orden ni preferencias. Cinco acciones acreditadas en http-contract.js y navegador; las comprobaciones adicionales posteriores se detallan abajo.

## Matriz auditada

| Fila del plan | Evidencia ejecutada y aprobada |
| --- | --- |
| Crear | xUnit CreateWorkItemUseCaseTests + HTTP missing/null/Low/Medium/High y lectura independiente. |
| Legado | Program.cs: NULL SQL auténtico antes de migrar y controles Low/Medium/High; default SQL y Low=0 EF. |
| Editar | xUnit nueve combinaciones, HTTP tres estados × tres niveles, navegador con recarga en cada estado. |
| Inválida | xUnit catálogo/números/ausencia de edición, HTTP 400 sin cambios, rechazo controlado en navegador. |
| Ausente | xUnit null, HTTP 404 y métricas/datos restantes intactos. |
| Concurrencia | Program.cs con dos contextos precargados, último guardado incluso igual al snapshot antiguo; SQL solo Priority y tokens Owner/Status; HTTP dos vistas previas. |
| Rechazo/red | Navegador: 400, respuesta incompleta y fallo de red, restauración/aviso/Actualizar; xUnit propagación sin retry y cancelación. |

## Corrección de una comprobación

La primera repetición del padre falló comparando CreatedAt de fábrica con una lectura PostgreSQL de menor precisión. La corrección compara dos snapshots persistidos, conservando la exigencia de que los cinco campos ajenos sean idénticos. La repetición de Program.cs terminó con todas las comprobaciones PASS y SQL capturado exclusivamente de Priority. No se cambió el código de aplicación para hacer pasar esa comparación.

## Repetición tras la revisión thorough

El padre ejecutó `browser-contract.mjs` mediante el tab CUA existente. Siete escenarios PASS: 400, fallo de red, 200 vacío, 200 con prioridad array (restauración, aviso y desbloqueo); PATCH confirmado seguido de GET fallido (conservar prioridad confirmada); foco trasladado al formulario de creación (no robar foco ni texto); GET A de prioridad superado por GET B de creación y A termina primero (B aún se aplica). El módulo usa únicamente fixtures UUID propias y retira intercepciones en finally. La primera carga CommonJS no era compatible con el import del runtime CUA; se convirtió el harness a ESM `.mjs`, sin tocar aplicación ni añadir librerías, y se ejecutó de nuevo con éxito.

Invocación reproducible en cua_repl, con `tab` del frontend aislado:

```javascript
const checker = await import('file:///C:/Users/Alejandra/Documents/JoseCarreno/projects/ejercicios-formacion/proyecto-codex-dotnet/scripts/verification/priority/browser-contract.mjs');
nodeRepl.write(await checker.checkPriorityBrowserContract(tab));
```

- Mensaje 400 actualizado: «La prioridad no es válida. No se guardó el cambio. Pulsa Actualizar para revisar el tablero.»
- UI real de reasignación/eliminación: crear «Verificación reasignación prioridad 20261014», guardar Alta, cambiar Ana→Bea con confirmación y recargar; tarjeta mostró Bea/Alta/Pendiente. Eliminar exclusivamente esa fixture con confirmación; tarjeta desapareció y métricas volvieron a Total 2/Pendientes 1/Completadas 1. También el checker eliminó solo las dos fixtures UUID creadas por él. Eliminaciones definitivas de datos de prueba, sin recuperación; se conservaron «Prueba de prioridad» y la tarea ajena «tarea nueva».
- Guardado real con respuesta perdida: interceptar en etapa Response el PATCH de «Prueba de prioridad», Alta→Media; se observó HTTP 200 antes de simular pérdida de respuesta. UI conservó Alta y aviso de incertidumbre, sin falso éxito ni retry. Tras Actualizar mostró Media persistida y los mismos contadores. No se presenta como prueba el intento anterior que coincidió con la caída del servicio de verificación.
- Suite completa repetida después de los parches: 71/71, cero fallos/omitidas; PostgreSQL y http-contract.js PASS; sintaxis app.js y browser-contract.mjs PASS.
- Riesgo residual heredado: fetch no tiene plazo máximo en las operaciones del tablero. Una conexión que nunca resuelve mantiene los controles en espera; los rechazos y fallos terminales sí liberan los controles. No se añadió una política global de timeout sin decisión de producto.
