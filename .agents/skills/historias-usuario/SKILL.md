---
name: historias-usuario
description: "Trigger: crear o refinar historias de usuario en Gherkin. Define contexto, alcance, reglas de negocio y aceptación; guarda un Markdown en el proyecto."
license: MIT
metadata:
  author: proyecto-codex-dotnet
  version: "1.0"
---

## Activation Contract

Usa esta skill al crear o refinar historias de usuario. Redacta en español salvo petición contraria. Crear una historia no autoriza implementar la funcionalidad.

## Hard Rules

- Fundamenta la historia en la petición, las convenciones y las fuentes funcionales disponibles del proyecto. Distingue comportamiento actual de comportamiento solicitado; el código no define por sí solo la intención de negocio.
- Define un resultado de valor, alcance incluido y exclusiones explícitas. No amplíes la petición.
- Especifica todas las reglas necesarias para ese alcance con identificadores RN-01, RN-02, etc., condiciones, restricciones, excepciones y resultados observables. Considera permisos, estados, datos obligatorios, límites y errores solo cuando apliquen.
- No inventes reglas, umbrales ni decisiones. Identifica contradicciones y pregunta por las decisiones que impidan criterios verificables. Si guardas un borrador, marca lo pendiente y su impacto; no lo presentes como listo.
- Escribe los criterios de aceptación en Gherkin válido y comprensible para negocio: `Dado` establece contexto, `Cuando` expresa la acción y `Entonces` el resultado observable. Evita detalles de implementación no exigidos.
- Guarda la historia como `.md` dentro del proyecto. Respeta una ubicación indicada; en otro caso usa la carpeta existente de historias o `docs/historias-usuario/<nombre-descriptivo>.md`. Evita rutas fuera del proyecto y sobrescrituras de historias ajenas.

## Decision Gates

| Situación | Acción |
| --- | --- |
| Falta una decisión de negocio | Formula una pregunta concreta y espera antes de cerrar la historia. |
| Se solicita un borrador con datos pendientes | Guarda con estado Borrador y decisiones pendientes visibles. |
| Ya existe la historia | Actualízala si se pidió refinarla; de lo contrario elige otro nombre. |
| Hay varios resultados independientes | Propón separarlos sin alterar el alcance autorizado. |

## Execution Steps

1. Revisa la petición y las fuentes pertinentes; registra sus rutas o referencias y las lagunas relevantes.
2. Explica actor, necesidad, valor, situación actual, objetivo, vocabulario, precondiciones y dependencias aplicables.
3. Delimita alcance y enumera reglas. Resuelve decisiones bloqueantes con el usuario.
4. Cubre cada regla con escenarios identificados CA-01, CA-02, etc.; incluye éxito, rechazo, excepciones y valores frontera aplicables. Usa `Esquema del escenario` y `Ejemplos` cuando aporten casos concretos.
5. Completa la estructura siguiente sin marcadores vacíos. Relaciona cada RN con al menos un CA; comprueba que cada CA tiene un resultado comprobable y ninguna regla contradice otra.
6. Guarda el archivo, léelo de nuevo y verifica estructura, cobertura y ruta. Comunica ubicación y pendientes. No afirmes haber ejecutado pruebas del producto al redactar criterios.

## Output Contract

Entrega un enlace al `.md` guardado. Usa esta estructura; sustituye los textos entre corchetes:

````markdown
# HU: [Título orientado al valor]

Estado: [Borrador o Lista para revisión]

## Contexto
[Problema, actor, situación actual, objetivo, términos, fuentes y dependencias]

## Historia de usuario
Como [actor], quiero [capacidad], para [beneficio].

## Alcance
### Incluido
[Comportamientos cubiertos]
### Fuera de alcance
[Exclusiones]

## Reglas de negocio
- RN-01: [Condición, restricción y resultado; fuente o decisión acordada]

## Criterios de aceptación
```gherkin
# language: es
Característica: [Capacidad de negocio]
  Regla: RN-01 - [Regla]
    Escenario: CA-01 - [Resultado observable]
      Dado [contexto concreto]
      Cuando [acción del actor]
      Entonces [resultado verificable]
```

## Cobertura
| Regla | Criterios |
| --- | --- |
| RN-01 | CA-01 |

## Decisiones pendientes
[Preguntas, impacto y criterios afectados, o Ninguna]
````
