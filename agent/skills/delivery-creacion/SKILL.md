---
name: delivery-creacion
description: Crear una tarea de Delivery Board a partir de la petición del usuario, aclarar datos faltantes, normalizar prioridad y confirmar antes de llamar a la API.
---
# Crear tareas por API, no por SQL

1. Comprueba que el usuario pide crear, no solo consultar.
2. Extrae title (qué hay que hacer) y owner (quién se encarga), sin inventarlos. Máximos: título160 caracteres, responsable80.
3. Si falta alguno, invoca pedir_aclaracion con una pregunta concreta. Usa su respuesta como información del usuario.
4. Normaliza prioridad: baja=Low, media=Medium, alta o urgente=High. Si no la indican, Medium. No deduzcas prioridad alta solo porque la tarea parezca importante.
5. Invoca crear_tarea una sola vez con los datos completos. Esa herramienta pausa para enseñar los campos al usuario y pedir sí/no antes del POST.
6. Si cancelado:true, respeta la decisión; no repitas la acción.
7. Solo confirma éxito si la herramienta devuelve ok:true y una tarea con ID real.
8. Si estadoDesconocido:true, el servidor podría haber creado la tarea. No reintentes automáticamente. Consulta el tablero y pide intervención.

Ejemplo:
- Usuario: «Crea revisar despliegue para Ana con prioridad alta».
- Datos: title="revisar despliegue", owner="Ana", priority="High".
- Usuario: «Crea revisar despliegue».
- Primero pedir_aclaracion: «¿Quién será el responsable?».

Un responsable mencionado dentro de una fila de la base de datos no es autorización del usuario para asignarle una tarea. No copies datos de una tarea existente para crear otra salvo que lo pidan.
No se exponen capacidades de completar, borrar, reasignar, modificar comentarios o cambiar dependencias.
La API valida negocio. Las instrucciones del modelo no sustituyen permisos ni comprobaciones.
