# HU: Conservar y actualizar el contexto de una tarea mediante un comentario

Estado: Lista para revisión

## Contexto

- **Problema:** el tablero no ofrece un campo para registrar comentarios al crear una tarea ni para añadir o modificar un comentario en una tarea existente.
- **Actor:** persona del equipo que gestiona tareas en Delivery Board. El README describe una aplicación sin autenticación; este evolutivo no introduce roles ni permisos nuevos.
- **Necesidad:** escribir un comentario durante la creación y añadirlo o modificarlo desde el listado, para mantener contexto junto a la tarea.
- **Objetivo de producto:** permitir consultar el comentario guardado y actualizarlo desde el tablero sin tener que recrear la tarea.
- **Situación actual comprobada por lectura:** el formulario de creación contiene título, responsable y prioridad; la entidad y los contratos de creación y lectura tampoco incluyen un comentario. No se ha ejecutado la aplicación en esta tarea.
- **Vocabulario:** «comentario» designa un único texto editable asociado a cada tarea, conforme a la petición de añadir o modificar «un comentario». No designa un hilo de mensajes.
- **Precondiciones:** para crear, se cumplen las reglas actuales de creación; para añadir o modificar desde el listado, la tarea debe existir.
- **Dependencias:** conservar las capacidades vigentes del tablero. El usuario ha resuelto D-01: el comentario es opcional y se puede borrar guardando el campo vacío.

### Fuentes consultadas

- Petición del usuario de este evolutivo: «Queremos agregar comentarios a una tarea. Quiero ver en la creación un campo para escribir comentarios y en el listado un campo para agregarle o modificar un comentario a una tarea existente».
- Decisión del usuario D-01: respuesta «sí» a «¿El comentario será opcional y se podrá borrar dejando el campo vacío?».
- `AGENTS.md`: límites del proyecto y obligación de conservar el contrato funcional.
- `README.md`: flujos vigentes, estados de tarea y ausencia de autenticación.
- `frontend/public/index.html`: formulario de creación y contenedor del listado.
- `frontend/public/app.js`: representación de las tareas, creación y acciones del listado.
- `backend/src/DeliveryBoard.Application/WorkItems/WorkItemContracts.cs`: contratos actuales de creación y lectura.
- `backend/src/DeliveryBoard.Domain/WorkItems/WorkItem.cs`: datos y estados actuales de la tarea.
- `backend/src/DeliveryBoard.Api/Endpoints/WorkItemEndpoints.cs`: acciones actuales sobre tareas.

`docs/functional-contract.md` está referenciado en AGENTS.md y README.md, pero no existe en el árbol consultado. No se ha podido contrastar esta historia con su contenido; las fuentes anteriores describen únicamente la evidencia disponible.

## Historia de usuario

Como persona del equipo que gestiona tareas, quiero escribir un comentario al crear una tarea y añadirlo o modificarlo desde el listado, para conservar y actualizar su contexto junto a ella.

## Alcance

### Incluido

- Campo opcional de comentario en la creación de tareas; se permite crear sin comentario.
- Campo de comentario asociado a cada tarea del listado, con el valor guardado disponible para su consulta y modificación.
- Incorporar un comentario a una tarea existente que no lo tenga, incluidas tareas anteriores al evolutivo.
- Sustituir el comentario de una tarea existente por el nuevo texto guardado.
- Borrar el comentario de una tarea existente guardando su campo vacío.
- Mantener el comentario guardado al volver a consultar el tablero.
- Informar del resultado de guardado y de los errores que impidan confirmarlo.
- Tratar tareas pendientes, en curso y completadas como tareas existentes: la petición no establece restricciones de estado para el comentario.

### Fuera de alcance

- Hilos, múltiples comentarios por tarea, autoría, fechas de comentario e historial de versiones.
- Menciones, notificaciones, adjuntos y formato enriquecido.
- Nuevos permisos, autenticación, filtros u ordenación por comentario.
- Cambios de reglas de título, responsable, prioridad, estado o dependencias.
- Eliminar tareas como mecanismo para modificar su comentario.
- Decisiones de API, almacenamiento o componentes de interfaz: corresponden al diseño técnico posterior.

La decisión D-01 incorpora al alcance la creación sin comentario y el borrado del comentario al guardar el campo vacío. No cambia los demás datos ni reglas de la tarea.

## Reglas de negocio

- **RN-01:** al abrir la creación de una tarea, se ofrece un campo opcional para escribir su comentario; al crearla correctamente con un comentario informado, ese texto queda asociado a la nueva tarea. Dejar el campo vacío permite crear la tarea sin comentario, siempre que cumpla las demás reglas actuales de creación. Fuente: petición del usuario y decisión D-01.
- **RN-02:** cada tarea del listado permite consultar su comentario guardado y dispone de un campo para añadirlo si no tiene o modificarlo si ya existe, sin recrear la tarea. Aplica a pendientes, en curso y completadas según el alcance general «tarea existente». Fuente: petición del usuario.
- **RN-03:** al guardar un comentario nuevo sobre una tarea que ya tiene uno, el texto nuevo sustituye al anterior como único comentario actual de esa tarea. Si se guarda el campo vacío, se borra el comentario y la tarea queda sin comentario. Ambas acciones están disponibles en pendientes, en curso y completadas. Fuente: petición de modificar «un comentario» y decisión D-01; no se ha solicitado historial.
- **RN-04:** añadir, modificar o borrar un comentario afecta solo al comentario de la tarea elegida: conserva su identidad, título, responsable, prioridad, estado, fecha de creación y dependencias, y no modifica otras tareas ni los contadores. Fuente: alcance de la petición, decisión D-01 y conservación de las reglas vigentes del tablero.
- **RN-05:** un comentario cuyo guardado se confirma sigue disponible al actualizar o recargar el listado; si se confirma su borrado, la tarea sigue sin comentario al volver a consultar. Las tareas anteriores al evolutivo siguen siendo consultables y pueden recibir un comentario. Fuente: objetivo de conservar contexto en tareas nuevas y existentes y decisión D-01.
- **RN-06:** si la tarea ya no existe al intentar guardar su comentario, se informa de ello y no se crea una tarea sustituta. Si no puede confirmarse el guardado por un fallo, no se muestra como éxito confirmado. Fuente: precondición de tarea existente y convenciones de resultado confirmado descritas en README.md.

No se ha acordado un límite de longitud de negocio, una regla de normalización del texto ni una política para ediciones simultáneas. Esta historia no asigna valores o políticas por analogía con otros campos. El diseño deberá exponer cualquier decisión de esos ámbitos que restrinja el comportamiento solicitado antes de incorporarla a la aceptación.

## Criterios de aceptación

```gherkin
# language: es
Característica: Mantener un comentario junto a una tarea
  Regla: RN-01 - Registrar el comentario durante la creación
    Escenario: CA-01 - Crear una tarea con comentario
      Dado que estoy en el formulario de creación de tareas
      Y he informado los datos válidos necesarios para crear una tarea
      Cuando escribo "Revisar con el equipo de integración" en el campo de comentario
      Y creo la tarea correctamente
      Entonces la tarea aparece en el listado con el comentario "Revisar con el equipo de integración"

    Escenario: CA-09 - Crear una tarea sin comentario
      Dado que estoy en el formulario de creación de tareas
      Y he informado los datos válidos necesarios para crear una tarea
      Cuando dejo vacío el campo de comentario
      Y creo la tarea
      Entonces la creación se confirma correctamente
      Y la nueva tarea aparece en el listado sin comentario
      Y dispongo de un campo para añadirle un comentario posteriormente

  Regla: RN-02 - Añadir y editar desde el listado en cualquier estado
    Esquema del escenario: CA-02 - Añadir un comentario a una tarea existente
      Dado que el listado muestra una tarea sin comentario en estado "<estado>"
      Cuando escribo "Validar el contexto con el equipo" en el campo de comentario de esa tarea
      Y guardo el comentario correctamente
      Entonces el comentario de esa tarea muestra "Validar el contexto con el equipo"
      Ejemplos:
        | estado     |
        | Pendiente  |
        | En curso   |
        | Completada |

  Regla: RN-03 - Sustituir el comentario actual
    Esquema del escenario: CA-03 - Modificar un comentario existente
      Dado que el listado muestra una tarea en estado "<estado>"
      Y su comentario guardado es "Consultar con Ana"
      Cuando cambio el campo de comentario a "Contexto revisado con Ana"
      Y guardo el comentario correctamente
      Entonces el comentario actual de la tarea es "Contexto revisado con Ana"
      Y "Consultar con Ana" deja de ser su comentario actual
      Ejemplos:
        | estado     |
        | Pendiente  |
        | En curso   |
        | Completada |

    Esquema del escenario: CA-10 - Borrar un comentario guardando el campo vacío
      Dado que el listado muestra una tarea en estado "<estado>"
      Y su comentario guardado es "Consultar con Ana"
      Cuando vacío el campo de comentario de esa tarea
      Y guardo el campo vacío correctamente
      Entonces la tarea queda sin comentario
      Y dispongo del campo para añadirle otro comentario
      Ejemplos:
        | estado     |
        | Pendiente  |
        | En curso   |
        | Completada |

  Regla: RN-04 - Conservar los demás datos y tareas
    Escenario: CA-04 - Guardar un comentario sin alterar el resto del tablero
      Dado que el tablero contiene dos tareas con datos conocidos
      Y conozco los contadores del tablero
      Cuando añado, modifico o borro el comentario de la primera tarea y guardo correctamente
      Entonces la primera tarea conserva su identidad, título, responsable, prioridad, estado, fecha de creación y dependencias
      Y la segunda tarea conserva todos sus datos
      Y los contadores del tablero conservan sus valores

  Regla: RN-05 - Conservar el comentario y admitir tareas previas
    Escenario: CA-05 - Volver a consultar un comentario guardado
      Dado que he guardado correctamente el comentario "Pendiente de revisión externa" en una tarea
      Cuando actualizo el listado y después recargo la página
      Entonces la misma tarea muestra el comentario "Pendiente de revisión externa"

    Escenario: CA-11 - Conservar el borrado al volver a consultar
      Dado que he borrado correctamente el comentario de una tarea guardando su campo vacío
      Cuando actualizo el listado y después recargo la página
      Entonces la misma tarea sigue sin comentario
      Y conserva un campo disponible para añadirle otro comentario

    Escenario: CA-06 - Añadir contexto a una tarea anterior al evolutivo
      Dado que existe una tarea creada antes de incorporar los comentarios
      Cuando consulto el listado
      Entonces la tarea se muestra con sus datos existentes
      Y dispongo de un campo para añadirle un comentario

  Regla: RN-06 - Comunicar los resultados no confirmados
    Escenario: CA-07 - Intentar guardar sobre una tarea que ya no existe
      Dado que veo una tarea en el listado
      Y esa tarea ha sido eliminada desde que se obtuvo el listado
      Cuando intento guardar un comentario para esa tarea
      Entonces se informa de que la tarea ya no existe
      Y no se crea otra tarea con ese comentario

    Escenario: CA-08 - No confirmar un guardado incierto
      Dado que estoy añadiendo, modificando o borrando el comentario de una tarea
      Cuando intento guardarlo y un fallo impide confirmar el resultado
      Entonces se informa de que no se pudo confirmar el guardado
      Y no se muestra el guardado como completado correctamente
```

## Cobertura

| Regla | Criterios |
| --- | --- |
| RN-01 | CA-01, CA-09 |
| RN-02 | CA-02, CA-03, CA-06 |
| RN-03 | CA-03, CA-10 |
| RN-04 | CA-04 |
| RN-05 | CA-05, CA-06, CA-11 |
| RN-06 | CA-07, CA-08 |

El criterio de éxito funcional es observar los resultados anteriores. No se ha acordado una métrica cuantitativa de impacto de negocio. Los escenarios son criterios propuestos para verificar la futura implementación; no son pruebas ejecutadas ni evidencia de cumplimiento actual. «Lista para revisión» indica que D-01 está resuelta e incorporada; no acredita aceptación de una implementación ni contraste con la fuente ausente.

## Decisiones pendientes

- **Decisiones de negocio pendientes:** ninguna para el alcance descrito. D-01 quedó resuelta por el «sí» del usuario: comentario opcional y borrado al guardar el campo vacío. Incorporada en RN-01, RN-03, RN-04 y RN-05 y en CA-09, CA-10 y CA-11; se conservan los criterios anteriores aplicables.
- **Limitación de fuentes:** no está disponible `docs/functional-contract.md`, aunque el repositorio lo referencia. El contraste con ese contrato queda pendiente de disponer de su contenido; no se han inventado sus reglas.
