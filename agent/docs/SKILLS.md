# Skills con createAgent

## ¿Existe un estándar?

Sí: **Agent Skills** define carpetas con `SKILL.md`, frontmatter YAML con `name` y `description`, y cuerpo Markdown con instrucciones. Puede haber referencias o scripts adicionales.

El estándar del archivo no significa que cualquier agente lo lea automáticamente. En LangChain, skills es un patrón de especialización por contexto. El soporte automático con descubrimiento y filesystem está en **Deep Agents**. No añadimos ese framework para conservar `createAgent` de la sesión14.

## Lo que implementa este proyecto

`src/skills.ts` lee exclusivamente el directorio `skills/` del proyecto. Valida el frontmatter y construye un catálogo. No permite que el modelo elija una ruta de disco, no ejecuta scripts y no modifica las herramientas autorizadas.

### Modo inline (por defecto)

```dotenv
SKILLS_MODE=inline
```

Las tres instrucciones completas se añaden al system prompt al crear el agente. Es la opción inicial para un modelo pequeño: no necesita decidir cargar una skill antes de poder trabajar. Ocupa más contexto y tokens. Reinicia el proceso cuando cambies los archivos.

### Modo progressive

```dotenv
SKILLS_MODE=progressive
```

El prompt contiene nombres y descripciones. La herramienta `cargar_skill` entrega el contenido de una skill cuando el modelo la pide. Así el mismo agente se especializa sin crear otro agente. El contenido queda como observación en el historial del hilo; no cambia el modelo ni añade permisos.

Esta es una implementación explícita del patrón, **no un parámetro `skills` de `createAgent`**, ni una implementación completa del runtime de Deep Agents. No hace falta ningún plugin de Codex para usar estas skills de la aplicación.

## Qué enseñan

| Skill | Contexto |
|---|---|
| delivery-consultas | PostgreSQL, comillas PascalCase, estados, conteos, filas y bloqueos correctos |
| delivery-creacion | Extracción, datos faltantes, prioridad y confirmación antes de API |
| delivery-react | Decisiones tool calling, observaciones y finalización |

El esquema real sigue viniendo del MCP, nunca se da por definitivo el ejemplo de una skill. Se corrigió la equivalencia incorrecta «tiene dependencias = bloqueada»: el contrato de la API exige Pending y alguna dependencia no completada.

## Qué no solucionan por sí solas

- Un modelo que no soporta herramientas nativas no adquiere ese soporte al leer un Markdown.
- Un modelo pequeño puede ignorar instrucciones, inventar argumentos o no terminar correctamente.
- Más texto puede empeorar rendimiento y atención. Compara inline/progressive con las mismas preguntas.
- Las instrucciones no sustituyen controles de autorización, solo lectura ni validación de dominio.

En este diseño el modelo ya no decide nombres de nodos del antiguo clasificador: `createAgent` controla el bucle fijo; el modelo elige herramientas. Esto evita esa tarea concreta de enrutamiento, pero sigue requiriendo tool calling fiable.

## Fuentes consultadas

- [Agent Skills specification](https://agentskills.io/specification).
- [Patrón skills con createAgent y load_skill](https://docs.langchain.com/oss/javascript/langchain/multi-agent/skills).
- [Skills integradas de Deep Agents](https://docs.langchain.com/oss/javascript/deepagents/skills).
- [MCPAdapter y createAgent](https://docs.langchain.com/oss/javascript/langchain/mcp).
