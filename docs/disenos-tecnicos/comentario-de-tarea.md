# Diseño técnico: conservar y actualizar el comentario de una tarea

Se propone añadir un único comentario opcional a cada tarea, capturarlo al crearla y editarlo mediante un campo y un botón de guardado en cada tarjeta del listado. Guardar vacío elimina el comentario. La implementación se divide en cinco tareas; este entregable autoriza su planificación, no inicia su ejecución.

## Identificación y fuente de la historia

- **Historia:** «Conservar y actualizar el contexto de una tarea mediante un comentario».
- **Fuente:** [comentario-de-tarea.md](../historias-usuario/comentario-de-tarea.md), estado «Lista para revisión», consultada el 16/10/2026, sin identificador ni versión formal.
- **Referencia de contenido:** SHA-256 `98CC842A22D0B274A15FD028CACF43EA1C013835D7163F35A72665AF7A5F1439`.
- **Base del repositorio:** HEAD `02918ce00100a453a188b7794182228e5bfd565c`; se inspeccionó el árbol de trabajo con cambios preexistentes, no una revisión limpia.
- **Decisión confirmada D-01:** comentario opcional; guardar el campo vacío borra el existente. Se conservan RN-01 a RN-06 y CA-01 a CA-11 de la historia.
- **Estado del diseño:** Planificado. No hay decisiones funcionales que impidan ejecutar el alcance definido. La ejecución queda pendiente de una petición que la autorice.
- **Fuentes adicionales:** `AGENTS.md`, `README.md`, código y pruebas enumerados abajo. Las definiciones de los agentes están en `.codex/agents/developer-backend.toml` y `.codex/agents/developer-front.toml`.

La historia registra que `docs/functional-contract.md` no estaba disponible. En esta revisión se comprobó que está eliminado en el índice y ausente del árbol de trabajo, pero se pudo leer su versión histórica mediante `git show HEAD:docs/functional-contract.md`. Ese contrato hasta sesión 9 exige conservar estados, dependencias, prioridades, métricas, borradores, foco y protección ante respuestas obsoletas. No se detecta conflicto con esta historia. No se restaura ni modifica el archivo eliminado; si se aporta otro contrato vigente, deberá contrastarse antes de ejecutar cambios que lo afecten.

## Objetivo y alcance técnico

Cubrir creación con/sin comentario, consulta, adición, sustitución y borrado en tareas pendientes, en curso y completadas, incluidas las anteriores a la migración. Persistir el resultado y comunicar errores o resultados inciertos sin afirmar éxito.

No introducir hilos, autores, fechas, historial, formato enriquecido, autenticación, filtros de comentario ni reglas adicionales sobre estado o dependencias. No cambiar las reglas actuales de otros campos. No incorporar librerías, frameworks de metodología ni artefactos `odd/`.

## Contexto y arquitectura actual

Las siguientes rutas y símbolos existen y fueron inspeccionados. Las adiciones se identifican expresamente en el diseño y las tareas.

| Componente existente | Responsabilidad observada e implicación |
| --- | --- |
| `frontend/public/index.html`, formulario `#work-item-form` | Crea con título, responsable y prioridad; no ofrece comentario. |
| `frontend/public/app.js`: `render`, `updateControls`, `beginMutation`, `loadDashboard`, `updateLocalItem`, `refreshAfterSuccess` | Renderiza tarjetas, mantiene bloqueos por tarea y versiones de lectura. `ownerDrafts` conserva los borradores de responsable. El POST serializa `FormData`; `changePriority` sirve de referencia para actualizar un campo sin apropiarse de otros cambios. |
| `frontend/public/styles.css` | Estilos comunes de inputs/selectores y adaptación a 760 px; falta estilo de textarea. |
| `frontend/public/board-view.js`: `BoardView.selectVisibleItems` | Filtros y orden vigentes; no necesitan cambios de comportamiento. |
| `backend/src/DeliveryBoard.Domain/WorkItems/WorkItem.cs`: `WorkItem.Create` | Identidad, título, responsable, prioridad, estado, creación y prerrequisitos; sin comentario. |
| `backend/src/DeliveryBoard.Application/WorkItems/WorkItemContracts.cs` y `WorkItemMappings.cs`: `ToResponse` | Contratos de creación, lectura y acciones; todas las respuestas de tareas pasan por el mapeo común. `GetDashboardUseCase` añade `IsBlocked` y calcula métricas. |
| `backend/src/DeliveryBoard.Domain/WorkItems/IWorkItemRepository.cs` y `backend/src/DeliveryBoard.Infrastructure/Persistence/WorkItemRepository.cs` | Persistencia; `ChangePriorityAsync` escribe exclusivamente prioridad mediante `ExecuteUpdateAsync`, y vuelve a leer sin seguimiento. Hay repositorios simulados que implementan esta interfaz en las pruebas. |
| `backend/src/DeliveryBoard.Infrastructure/Persistence/WorkItemConfiguration.cs` | Tabla `work_items`; Owner y Status son tokens de concurrencia. Prerrequisitos son un array PostgreSQL. |
| `backend/src/DeliveryBoard.Infrastructure/Persistence/Migrations/` | Migraciones y `DeliveryBoardDbContextModelSnapshot.cs`; la última inspeccionada añade dependencias. |
| `backend/src/DeliveryBoard.Api/Endpoints/WorkItemEndpoints.cs`: `MapWorkItemEndpoints` | Minimal APIs de creación, tablero y acciones por tarea. |
| `backend/src/DeliveryBoard.Infrastructure/DependencyInjection.cs`: `AddApplicationAndInfrastructure` | Registro de casos de uso y repositorio. |
| `backend/src/DeliveryBoard.Api/Program.cs`: `ApplyMigrationsAsync`; `Errors/GlobalExceptionHandler.cs` | Migración al arrancar; errores esperados 400/409/422 y error inesperado 500 con log sin exponer detalles internos. |
| `backend/tests/DeliveryBoard.Application.UnitTests/WorkItems/` | xUnit de casos de uso con repositorios simulados. No acredita persistencia real. |
| `frontend/tests/` y `scripts/verification/priority/` | Tests Node sin paquetes y verificaciones explícitas PostgreSQL/HTTP/navegador que sirven de patrón, sin atribuirles resultados de esta revisión. |

## Diseño propuesto y decisiones

### Datos, creación y lectura

Añadir **`WorkItem.Comment` de tipo `string?`** y un método de dominio propuesto `ChangeComment(string? comment)`, aplicable en cualquier estado. Representar ausencia como `null`; convertir únicamente la cadena de longitud cero a ausencia. Conservar los demás textos, sus espacios y saltos de línea: no aplicar `Trim`, no interpretar espacios como borrado ni imponer una longitud de negocio que el usuario no haya definido.

Extender `WorkItem.Create` con un parámetro opcional final de comentario y `CreateWorkItemRequest` con `string? Comment = null` al final, para conservar llamadas y clientes existentes. El caso de uso de creación entrega el comentario al dominio antes de `AddAsync`. Extender `WorkItemResponse` con `Comment` al final y actualizar `ToResponse`; así el tablero y las demás acciones devuelven también el comentario persistido. El cliente interpreta `null` o propiedad ausente como campo vacío.

Mapear `Comment` a columna nullable PostgreSQL `text`, sin índice, default ni límite artificial. Crear una migración **nueva**, nombre propuesto `AddWorkItemComment`, con su diseñador y snapshot actualizado. `Up` añade la columna sin reescribir los otros datos ni las dependencias; las filas previas quedan con NULL. `Down` elimina solo esa columna y por tanto pierde los comentarios: reservarlo para pruebas aisladas o recuperación con copia previa. No ejecutar rollback de datos reales como parte de la verificación.

### Contrato HTTP de actualización

Nueva acción propuesta `PATCH /api/work-items/{id:guid}/comment`, caso de uso `ChangeWorkItemCommentUseCase` e interfaz `IChangeWorkItemCommentUseCase`, siguiendo la organización actual.

| Operación | Entrada y salida propuestas |
| --- | --- |
| `POST /api/work-items` | Campos actuales más `comment` opcional. Omitido, null o `""` crea sin comentario. `201` devuelve `WorkItemResponse` con `comment`. Las validaciones actuales siguen vigentes. |
| `GET /api/work-items/dashboard` | Cada elemento incluye `comment: string|null`; sin cambiar métricas, orden ni bloqueo. |
| `PATCH /api/work-items/{id}/comment` | `ChangeWorkItemCommentRequest(string? Comment)`. Cuerpo `{ "comment": "texto" }` sustituye; `{ "comment": "" }` y `{ "comment": null }` borran. Por la nulabilidad del DTO, `{}` equivale a ausencia y borra; el frontend siempre envía la propiedad explícita. No es un JSON Patch genérico. |
| Respuesta correcta | `200` con `WorkItemResponse` persistido. Permite los tres estados. Repetir una sustitución no crea tareas ni comentarios adicionales. |
| Tarea inexistente | `404`, sin creación alternativa; incluye el caso de cero filas afectadas al escribir. |
| Cuerpo inválido o comentario de tipo distinto de string/null | `400` de binding, sin escritura. GUID no compatible con la restricción de ruta conserva la semántica actual de enrutamiento. |
| Fallo inesperado | `500` conforme al manejador existente; el cliente comunica resultado no confirmado. Sin reintentos automáticos. |

Registrar la nueva dependencia en `AddApplicationAndInfrastructure` y el endpoint en `MapWorkItemEndpoints`, con nombre y metadatos de respuesta consistentes con el proyecto. No añadir roles, autorización ni nuevos códigos de negocio para comentarios.

### Persistencia aislada y concurrencia

Añadir al repositorio el método propuesto `ChangeCommentAsync(Guid id, string? comment, CancellationToken cancellationToken)`, que actualiza **solo Comment por Id** y devuelve la tarea leída sin seguimiento, o null si ya no está disponible. Reutilizar el patrón de actualización de prioridad; no guardar una entidad reconstruida desde el cliente. Propagar cancelación, no reintentar y adaptar todos los dobles de `IWorkItemRepository` que deban compilar con el nuevo miembro.

No añadir tokens de concurrencia, comparación de comentario esperado ni bloqueos del grafo para este campo: la historia no solicita detectar ediciones simultáneas. Con actualizaciones ordinarias sobre la misma fila, una escritura posterior puede sustituir otra; la relectura posterior puede observar ese valor posterior y no constituye una instantánea ni historial. Esta es una limitación técnica explícita, no una nueva garantía de negocio. Si el producto exige avisar de sobrescrituras, debe volver al PO antes de ampliar el contrato. Owner y Status conservan sus tokens; las otras acciones no deben escribir Comment desde snapshots obsoletos. T-04 comprueba esa separación con PostgreSQL real.

Se descarta un repositorio de múltiples comentarios porque contradice el único texto actual de la historia. Se descarta reutilizar un PATCH de tarea completa porque expondría campos ajenos y pondría en riesgo RN-04.

### Interfaz y flujo de guardado

1. Añadir un `textarea` etiquetado **Comentario (opcional)** al formulario de creación, sin `required` ni `maxlength`. Se incluye en el POST mediante `FormData`; se conserva ante fallo y se limpia con el reset de creación confirmada.
2. Renderizar un textarea y botón **Guardar comentario** por tarjeta, incluidos los tres estados. El valor inicial es el texto guardado o vacío. Mostrar ayuda breve: «Guardar vacío borra el comentario». No guardar al teclear o perder foco ni añadir un diálogo de confirmación que la historia no exige.
3. Mantener borradores de comentario en memoria por Id, independientes de `ownerDrafts`, para que rerenders, filtros o actualizaciones no eliminen lo escrito. Conservarlos ante fallos; retirarlos al guardar confirmado o desaparecer la tarea. El estado Completada no descarta estos borradores. No persistirlos en localStorage.
4. Integrar inputs/botón en `updateControls` y el bloqueo existente de `beginMutation`/`endMutation`: acciones de la misma tarjeta y Actualizar se bloquean mientras guarda; otras tareas conservan el comportamiento vigente. Extender restauración de foco y selección a los textarea sin apropiarse del foco si el usuario cambió de control.
5. Enviar PATCH explícito, sin mutación optimista del valor confirmado. Confirmar únicamente con `200`, JSON interpretable, Id coincidente y propiedad `comment` presente de tipo string o null. Aplicar localmente solo el comentario devuelto sobre la tarjeta actual; el refresco posterior reconcilia los demás datos. No atribuir al guardado cambios de estado/contadores recibidos en una respuesta de otro cliente.
6. Ante 404 informar «La tarea ya no existe», conservar la semántica de eliminación/reconciliación del tablero y no crear otra tarea. Ante fallo de red, error HTTP o cuerpo incompleto conservar el borrador y mostrar que no se confirmó el guardado, con indicación de Actualizar; sin repetir automáticamente. Una lectura de reconciliación no confirma retrospectivamente una respuesta perdida como éxito de aquella petición.
7. Si el PATCH queda confirmado pero falla el refresco, conservar el comentario confirmado y distinguir «Comentario guardado» de «No se pudo actualizar el tablero». Invalidar lecturas antiguas mediante `dashboardVersion`, como las otras acciones.

### Seguridad, rendimiento y compatibilidad

Tratar el contenido como texto: interpolar usando `escapeHtml` o asignar `.value`/`.textContent`; probar especialmente `</textarea>`, etiquetas y comillas. No registrar el comentario en logs. Conservar la política de acceso local y el manejador de errores existentes. Incluir textarea en estilos, foco visible, etiquetas asociadas y anuncios de resultado; evitar desbordamientos de textos largos a escritorio y tablet.

La consulta del tablero añade una columna y la edición una escritura por Id más la lectura existente del patrón; no añadir consultas por tarjeta. Sin búsqueda por comentario, no se necesita índice. No introducir un recorte silencioso ni un límite arbitrario; continúan los límites técnicos generales del servidor y del navegador. La compatibilidad de clientes previos se conserva con entradas opcionales y respuestas aditivas; la base debe migrarse antes de servir la API nueva, como hace el arranque actual. La API anterior puede ignorar la columna extra; una vuelta atrás de código no exige borrar comentarios de la base.

## Tareas y asignaciones

Las asignaciones a agentes definidos en el proyecto son propuestas de responsabilidad, sin ejecución iniciada ni compromiso de disponibilidad o fechas. Cada agente deberá leer este diseño y la historia antes de editar; no revertir cambios de otros colaboradores.

| ID | Tarea | Responsable/rol propuesto | Dependencias | Estado |
| --- | --- | --- | --- | --- |
| T-01 | Incorporar comentario al modelo, persistencia, creación y lectura | `developer-backend` | Ninguna | Completada |
| T-02 | Exponer sustitución/borrado aislado por API | `developer-backend` | T-01 | Completada |
| T-03 | Implementar captura y edición por tarjeta con resultados fiables | `developer-front` | Ninguna; contrato fijado en este diseño | Pendiente |
| T-04 | Acreditar migración, API y aislamiento de escrituras reales | `developer-backend` | T-01, T-02 | Completada |
| T-05 | Verificar flujo integrado y registrar uso y cumplimiento | Team Leader; navegador a `developer-front` | T-03, T-04 | Pendiente |

### T-01 — Modelo, persistencia, creación y lectura

- **Objetivo/resultado:** tareas nuevas y antiguas admiten un texto opcional persistente y las respuestas lo incluyen.
- **Archivos:** `WorkItem.cs`, `WorkItemContracts.cs`, `WorkItemMappings.cs`, `CreateWorkItemUseCase.cs`, `WorkItemConfiguration.cs` en las rutas existentes de contexto; nueva migración `AddWorkItemComment` y diseñador en `Persistence/Migrations/`, actualización de `DeliveryBoardDbContextModelSnapshot.cs`; `CreateWorkItemUseCaseTests.cs` y `GetDashboardUseCaseTests.cs` existentes, ampliados donde corresponda. No modificar migraciones anteriores.
- **Finalización:** creación con texto, omitido, null y vacío; lectura con el mismo comentario; preservación de espacios, saltos, Unicode y resto de campos; firmas anteriores siguen compilando. Migración y snapshot coherentes; validación real de migración reservada a T-04.
- **Comprobaciones:** pruebas unitarias de creación/lectura y dominio implicado; compilación y suite .NET. CA-01, CA-06 y CA-09, parte persistente de CA-05/CA-11.

### T-02 — Acción de comentario y escritura acotada

- **Objetivo/resultado:** PATCH de texto o vacío en todos los estados, sin modificar otros campos y con 404 al no existir.
- **Archivos existentes:** `IWorkItemRepository.cs`, `WorkItemRepository.cs`, `WorkItemContracts.cs`, `WorkItemEndpoints.cs`, `DependencyInjection.cs`; dobles del repositorio en `backend/tests/DeliveryBoard.Application.UnitTests/WorkItems/*.cs` y scripts que implementen la interfaz y deban seguir compilando.
- **Archivos nuevos propuestos:** `backend/src/DeliveryBoard.Application/WorkItems/IChangeWorkItemCommentUseCase.cs`, `ChangeWorkItemCommentUseCase.cs`; `backend/tests/DeliveryBoard.Application.UnitTests/WorkItems/ChangeWorkItemCommentUseCaseTests.cs`.
- **Finalización:** dominio sin restricción de estado; consulta/escritura con cancelación; reemplazo y borrado normalizados según contrato; solo actualiza Comment; errores propagados sin reintentos; DI y endpoint accesibles. Ajustes de dobles incluidos en esta misma tarea para mantener compilación.
- **Comprobaciones:** matriz Pending/InProgress/Completed para añadir, sustituir y borrar; Id ausente; cancelación antes de escribir; fallo de repositorio; preservación de identidad, todos los campos, dependencias y otra tarea. Suite .NET. CA-02, CA-03, CA-04, CA-07 y CA-10; HTTP real en T-04.

### T-03 — Formulario y listado

- **Objetivo/resultado:** campo opcional al crear y editor siempre disponible en cada tarea; feedback, borradores, accesibilidad y bloqueo coherentes.
- **Archivos:** `frontend/public/index.html`, `app.js`, `styles.css`; nuevo `frontend/tests/comments.test.js`, siguiendo el patrón Node/VM actual. Ajustar fixtures de tests existentes solo si lo requiere el nuevo renderizado; no alterar reglas de `board-view.js`.
- **Finalización:** cubrir los siete pasos del flujo de interfaz, vacío/null, resultado no confirmado y refresco posterior fallido. Conservar borradores de responsable, filtros, orden, métricas y protección ante lecturas antiguas; tratar texto como texto; ninguna restricción de comentario por estado.
- **Comprobaciones:** tests Node con respuestas controladas para 200 válido, 404, error, JSON truncado/tipo inválido/Id distinto y red; bloqueo/doble envío y lectura obsoleta; comentarios escapados, mantenimiento de foco y borradores. Pruebas de filtros y preferencias existentes siguen pasando. Verificación real de navegador en T-05.

### T-04 — Migración y contrato con PostgreSQL aislado

- **Objetivo/resultado:** evidencia repetible de persistencia real y de que las escrituras no dañan otros datos ni reglas.
- **Archivos nuevos propuestos:** `scripts/verification/comments/Verification.csproj`, `Program.cs`, `http-contract.js`, `README.md`, tomando el patrón de `scripts/verification/priority/` sin cambiar las credenciales ni la base de aquella verificación. Propiedad de estos archivos: backend.
- **Finalización:** documentar entorno exclusivo local y fixtures identificadas; probar base nueva y base en migración previa con tarea existente, migrar y conservar sus datos; añadir/modificar/borrar y releer en otro contexto; HTTP POST/GET/PATCH y binding inválido; 404 tras eliminación de fixture; contadores y segunda tarea intactos. Especificar limpieza exclusiva de fixtures propias, sin volúmenes/contenedores ajenos.
- **Comprobaciones:** captura de SQL o inspección equivalente para acreditar UPDATE exclusivo de Comment; intercalar cambios de responsable/estado/prioridad/dependencias con comentario y comprobar que no se sobrescriben entre sí; no presentar dos actualizaciones sin carrera controlada como prueba de concurrencia. Sin prometer detección de conflicto entre dos comentarios. Incluir comprobación de regresión de los tokens de Owner/Status. Repetir verificaciones existentes de inicio/prioridad/dependencias aplicables en sus entornos aislados y registrar resultados.
- **Alcance de evidencia:** CA-01 a CA-07 y CA-09 a CA-11 en las capas API/persistencia; CA-08 requiere también fallos controlados del cliente en T-03/T-05.

### T-05 — Integración, uso y cierre técnico

- **Objetivo/resultado:** evidencia de los criterios en la aplicación integrada y documentación de la nueva acción.
- **Archivos:** `README.md` para uso/contrato público; este diseño para estados, fecha/revisión evaluada y resultados. Evidencia de navegador propuesta en `scripts/verification/comments/browser-verification.md`, propiedad de frontend durante su comprobación. Team Leader actualiza únicamente este diseño al consolidar.
- **Finalización:** comprobar navegador embebido de Codex en escritorio y tablet (por ejemplo, 1280×800 y 768×1024), teclado, foco, texto largo y saltos. Recorrer CA-01 a CA-11, incluyendo tareas anteriores, los tres estados, desaparición de fixture, pérdida de respuesta y refresco fallido tras guardado confirmado. Registrar limitaciones reales, sin aceptar funcionalmente en nombre del PO.
- **Comprobaciones:** ejecutar suites aplicables una vez integrada la solución, releer instrucciones de verificación y contrato HTTP documentado; conservar hooks existentes. Evaluar cada criterio con evidencia nueva. Team Leader declara Cumple/No cumple/No verificable y entrega la aceptación funcional al PO.

## Dependencias y orden de ejecución

Orden propuesto: T-01 → T-02 → T-04 → T-05. T-03 puede desarrollarse en paralelo desde este contrato, usando respuestas simuladas hasta disponer de T-02; converge con T-04 en T-05. No existen ciclos.

Backend mantiene propiedad de dominio, contratos, API, infraestructura, pruebas .NET y scripts de persistencia/HTTP. Frontend mantiene HTML/JS/CSS, pruebas Node y evidencia de navegador. T-01 y T-02 son secuenciales porque comparten contratos; T-04 no debe modificar archivos de T-03. En T-05 se reparte README (coordinación), evidencia de navegador (frontend) y diseño (Team Leader), sin escritores simultáneos sobre el mismo archivo.

## Bloqueantes y resolución

No se identificaron bloqueantes técnicos ni decisiones de negocio pendientes que impidan ejecutar este diseño. La ausencia del contrato en el árbol queda como limitación de fuente, parcialmente resuelta al leer su versión HEAD; no se inventa ni restaura su contenido como archivo vigente.

La autorización actual termina en planificación. La implementación y las pruebas que crean datos o arrancan servicios no se han iniciado. Para T-04/T-05 se necesitará un entorno local aislado de PostgreSQL/API/frontend; su disponibilidad no ha sido comprobada, por lo que no se declara un fallo ni un bloqueo observado. Si no está disponible al ejecutar, registrar B-01 con causa, tarea afectada, responsable de entorno, acción y prueba de desbloqueo, sin usar datos ajenos como alternativa.

## Estrategia de verificación

Se consultó `.agents/skills/run-tests/SKILL.md` antes de determinar comandos. No existe overlay `.agents/skill-overlays/dotnet-test/run-tests.md` ni `global.json` en el árbol inspeccionado. `DeliveryBoard.Application.UnitTests.csproj`, `backend/Directory.Build.props` y `Directory.Packages.props` establecen net10.0, xUnit 2.9.3 y Microsoft.NET.Test.Sdk 17.14.1: comando convencional VSTest, sin opciones MTP. Requiere SDK .NET 10 según README. No se ha inferido TDD de la presencia de tests ni se ha verificado un modo TDD explícito; al ejecutar se respetará la configuración vigente y se harán comprobaciones funcionales en cualquier caso.

Comandos de cierre propuestos, desde la raíz, para la futura implementación:

```powershell
dotnet test backend/DeliveryBoard.slnx
node --test frontend/tests/*.test.js
```

La suite .NET restaura y compila; no sustituye PostgreSQL ni navegador. Node comprueba lógica con dobles, no DOM/layout reales. Tras crear los scripts de T-04, su README deberá ofrecer comandos exactos con el entorno aislado ya validado. La generación de migración utilizará la herramienta local de `backend/.config/dotnet-tools.json`, como indica README; no requiere instalación global. No aplicar migraciones ni ejecutar scripts destructivos sobre la base habitual para acreditar una prueba.

Pruebas prioritarias: matrices de estados y borrado, compatibilidad con filas/clientes previos, conservación de campos/dependencias/métricas, persistencia tras nuevo contexto y recarga, texto literal sin ejecución HTML, borradores y foco, bloqueos, lecturas obsoletas y errores inciertos. Registrar comando real, salida, código de retorno y conteos cuando proceda; un build fallido no equivale a tests ejecutados. No reutilizar evidencia histórica como prueba actual.

## Trazabilidad de aceptación

Toda la evidencia de ejecución está pendiente; la revisión de código acredita la necesidad y viabilidad del diseño, no el cumplimiento implementado.

| Criterio de la historia | Regla | Decisión/tareas | Evidencia exigida y estado |
| --- | --- | --- | --- |
| CA-01: creación con comentario | RN-01 | Campo opcional, modelo y respuesta: T-01, T-03, T-04, T-05 | Unitarias, POST/GET y navegador; pendiente. |
| CA-02: añadir en tres estados | RN-02 | PATCH sin restricción de estado: T-02, T-03, T-04, T-05 | Matriz de tres estados, API y tarjetas; pendiente. |
| CA-03: sustituir en tres estados | RN-02, RN-03 | Único Comment reemplazable: T-02, T-03, T-04, T-05 | Matriz; nuevo valor sustituye al anterior; pendiente. |
| CA-04: conservar resto del tablero | RN-04 | UPDATE de una columna y autoridad local acotada: T-02, T-03, T-04, T-05 | Snapshot de campos, dependencias, segunda tarea, métricas y SQL; pendiente. |
| CA-05: persistir tras actualizar/recargar | RN-05 | Columna persistida y lecturas: T-01, T-03, T-04, T-05 | Nuevo contexto, GET y recarga real; pendiente. |
| CA-06: tarea anterior al evolutivo | RN-02, RN-05 | Migración nullable y editor vacío: T-01, T-03, T-04, T-05 | Fixture con esquema previo y migración; listado editable; pendiente. |
| CA-07: tarea desaparecida | RN-06 | 404 sin alta alternativa: T-02, T-03, T-04, T-05 | Eliminar fixture antes de PATCH, mensaje y ausencia de POST; pendiente. |
| CA-08: no confirmar resultado incierto | RN-06 | Validar respuesta, conservar borrador, sin reintento: T-03, T-05 | Fallos de red/HTTP/cuerpo y refresh fallido distinguidos; pendiente. |
| CA-09: creación sin comentario | RN-01 | Omitido/null/vacío compatibles: T-01, T-03, T-04, T-05 | Unitarias, HTTP y formulario vacío; pendiente. |
| CA-10: borrar en tres estados | RN-03 | Vacío → null, mismo PATCH y campo disponible: T-02, T-03, T-04, T-05 | Matriz de estados, SQL y editor; pendiente. |
| CA-11: persistir el borrado | RN-05 | Ausencia persistida y relectura: T-01, T-02, T-03, T-04, T-05 | NULL, nuevo contexto y recarga real; pendiente. |

## Progreso, cumplimiento y próximos pasos

- **Planificación entregada:** revisión de historia, reglas y código; contraste del contrato histórico; diseño y cinco tareas con responsables propuestos, dependencias y verificaciones. Entregable único de esta historia.
- **Implementación:** 0 de 5 tareas completadas; las cinco permanecen Pendiente. No se ha delegado su ejecución ni cambiado código de aplicación, pruebas, migraciones o la historia.
- **Comprobaciones realizadas el 16/10/2026:** lectura de fuentes/configuración, referencia HEAD y hash de historia; verificación de existencia de rutas actuales, ausencia de colisión del entregable y contrato histórico. Relectura del documento guardado para comprobar las diez secciones, cobertura CA-01…CA-11, dependencias sin ciclos y estados coherentes.
- **Pruebas ejecutadas en esta planificación:** ninguna; compilación, tests .NET/Node, PostgreSQL, HTTP y navegador quedan pendientes para implementación. No hay fallos de pruebas observados. La primera lectura de la herramienta EF en `.config/` raíz no encontró archivo; se resolvió localizándolo en `backend/.config/dotnet-tools.json`.
- **Limitaciones:** contrato eliminado en el árbol actual; disponibilidad de entorno no verificada. Herramientas de Engram no expuestas en esta sesión: no se persistió copia de memoria ni se instaló integración.
- **Resultado técnico de la funcionalidad:** **No verificable**, porque no está implementada ni probada en esta revisión. No constituye aceptación funcional.
- **Siguiente acción:** cuando se solicite implementar, asignar T-01/T-02 a backend y T-03 a frontend con este contrato; después T-04 y T-05. Confirmar funcionalmente con el PO a partir de las evidencias resultantes. Sin fechas comprometidas ni operaciones remotas.

## Ejecución backend autorizada — 16/10/2026

La petición posterior «Implementa la historia de usuario que ha diseñado el Team leader» autoriza la ejecución backend. Este apartado y las filas T-01/T-02/T-04 actualizan los estados históricos de planificación anteriores. Frontend/T-05 y la aceptación funcional requieren consolidación por sus responsables; backend no declara su cumplimiento.

- **T-01 — Completada:** Comment nullable, parámetro final opcional de creación, mapeo compartido y columna PostgreSQL text. Migración nueva `20261015224116_AddWorkItemComment` con diseñador/snapshot, sin cambios a migraciones previas. Creación y lectura cubren texto literal, Unicode/saltos/espacios, null, vacío y cliente previo. Migración desde cero y desde dependencias con fixture antigua comprobadas con PostgreSQL exclusivo. CA-01/CA-05/CA-06/CA-09/CA-11 en backend.
- **T-02 — Completada:** caso de uso e interfaz, registro DI y PATCH `/api/work-items/{id}/comment`; parámetros explícitos y cancelación. UPDATE exclusivo de Comment con relectura sin seguimiento, sin reintentos ni cambios de otros campos. Matriz de tres estados, alta/sustitución/borrado, cancelación y fallos de repositorio. API comprueba 404 tras desaparición y 400 ante binding inválido. Se corrigió GlobalExceptionHandler para conservar el 400 de BadHttpRequestException con mensaje público seguro; antes devolvía 500 en Development. CA-02/CA-03/CA-04/CA-07/CA-10 en backend.
- **T-04 — Completada (API/persistencia):** scripts repetibles en `scripts/verification/comments/`; PostgreSQL exclusivo en 55436 y API exclusiva en 15236. PASS fresh y legacy; nuevas lecturas de contexto, SQL capturado, snapshots precargados e intercalación de comentario/responsable/estado/prioridad/dependencias sin sobrescrituras ajenas y tokens Owner/Status conservados. PASS 83 comprobaciones HTTP de comentarios, regresión HTTP de prioridad/inicio/acciones previas y PASS 34 comprobaciones de dependencias incluida concurrencia. No se ejecutaron los harness históricos de persistencia de prioridad/inicio con sus otras conexiones; el alcance de regresión acreditado es el indicado. Sin prueba de navegador ni CA-08 de cliente.
- **Compilación:** `dotnet build backend/DeliveryBoard.slnx --configuration Release`, código 0, 0 advertencias/errores. **Suite:** `dotnet test backend/DeliveryBoard.slnx --configuration Release`, código 0; Passed: 114, Failed: 0, Skipped: 0; runner VSTest/xUnit 2, TDD sin configuración explícita. También pasó Debug con 114/0/0.
- **Modelo EF:** `dotnet ef migrations has-pending-model-changes --project src/DeliveryBoard.Infrastructure --startup-project src/DeliveryBoard.Api --configuration CommentsVerification --no-build`, desde backend, código 0 y sin cambios pendientes. `git diff --check`, código 0.
- **Limpieza:** fixtures HTTP eliminadas (COUNT final 0), bases temporales propias eliminadas en finally; API y contenedor de comprobación propios retirados. Se conservaron procesos, datos y contenedores ajenos; sin commits, operaciones remotas, instalaciones globales ni artefactos ODD.
- **Incidencias resueltas/limitaciones:** API preexistente bloqueaba DLL Debug; se conservó y compiló en salida separada/Release. Intentos sandbox de acceso local fallaron y se repitieron con acceso autorizado. Configuración personalizada de solución no válida se corrigió a Release; ruta inicial de EF corregida. Detalles, comandos reproducibles y evidencia en `scripts/verification/comments/README.md`; ejemplos manuales en `backend/src/DeliveryBoard.Api/comments.http`. Engram no disponible. Pendientes: interfaz/navegador y consolidación global por Team Leader, sin aceptación funcional del producto.
