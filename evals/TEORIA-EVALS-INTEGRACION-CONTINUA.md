# Evals e integración continua para agentes

## ¿Qué vamos a aprender hoy?

Vamos a utilizar **Delivery Board Agent**, el agente que ya hemos construido,
para comprobar su comportamiento de una forma repetible:

1. Crear un eval y ver su resultado en la consola local.
2. Guardar el caso y analizar sus resultados en LangSmith.
3. Preparar su ejecución automática mediante GitHub Actions.

> La idea de la sesión: pasar de «parece que funciona» a «tenemos una
> comprobación que podemos repetir después de cada cambio».

---

## 1. ¿Qué es un eval?

Un **eval**, de *evaluation*, es una comprobación que mide si una aplicación
de IA cumple un criterio definido.

Tiene tres piezas:

| Pieza | Ejemplo de Delivery Board |
| --- | --- |
| Entrada | «¿Cuántas tareas tiene EvalSesion?» |
| Referencia | Sabemos que tiene tres tareas de laboratorio. |
| Evaluador | Compara el total obtenido con el esperado y asigna una puntuación. |

Si responde con el total correcto, nuestro ejemplo puntúa **1**. Si no, **0**.
No estamos preguntando al agente si cree que respondió bien: lo comprobamos
con una regla independiente.

**Eval no es el nombre de una librería.** Es una práctica que podemos implementar
con código y organizar con herramientas como LangSmith.

## 2. ¿Es como un test unitario?

Se parece: hay un caso, un resultado esperado y una comprobación. Pero un eval
puede evaluar mucho más que una función aislada:

- Una respuesta del modelo.
- El efecto de un prompt o una skill.
- Las decisiones y herramientas de un agente completo.

Por eso algunos evals se parecen más a pruebas de integración. No siempre
exigen una frase exacta: distintas respuestas pueden ser correctas.

En nuestra práctica pediremos un número para facilitar la comparación.
**Solo evaluaremos el resultado:** acertar el número no demuestra por sí solo
que el agente haya utilizado bien todas sus herramientas.

## 3. ¿Por qué son importantes para los agentes?

El comportamiento depende de varias piezas que pueden cambiar:

```text
Modelo + prompt + skills + herramientas + datos + grafo
```

Cambiar una skill puede mejorar una consulta y empeorar otra. Cambiar el modelo
puede alterar cómo solicita herramientas. Incluso una misma petición puede
producir respuestas diferentes entre ejecuciones.

Los evals ayudan a detectar **regresiones**: comportamientos que funcionaban y
dejan de funcionar después de un cambio. También permiten comparar alternativas
sin depender únicamente de impresiones personales.

Un solo caso no garantiza la calidad del agente. Hoy empezaremos con uno para
entender la dinámica; después podremos ampliar los casos y repetirlos.

---

## 4. ¿Qué es la integración continua —CI—?

La **integración continua** consiste en ejecutar comprobaciones automáticas
cuando incorporamos cambios al código.

Con GitHub Actions podemos preparar un entorno, instalar dependencias,
compilar lo necesario y ejecutar el eval:

```text
Cambio en el repositorio
          ↓
Preparar el entorno
          ↓
Ejecutar las comprobaciones y el eval
          ↓
¿Cumple los criterios?
     ↙             ↘
    Sí             No
Job aprobado    Job fallido
```

Esto permite detectar problemas antes de publicar una nueva versión. Para
bloquear también una fusión, hay que configurar la comprobación como obligatoria
en la política de la rama; una puntuación en LangSmith no lo hace automáticamente.

**CI no significa desplegar.** El despliegue pertenece a la entrega o despliegue
continuos —CD—. En esta práctica nos centraremos en validar, no en publicar una
aplicación en producción.

## 5. ¿Qué aporta cada herramienta?

| Pieza | Responsabilidad |
| --- | --- |
| LangChain/LangGraph | Ejecutar nuestro agente y sus herramientas. |
| Eval | Comprobar el criterio y producir una puntuación. |
| LangSmith | Organizar casos, registrar experimentos y analizar sus resultados. |
| GitHub Actions | Automatizar la preparación del entorno y la ejecución. |

En local reutilizaremos la aplicación que levantamos con **Aspire**.
En CI habrá que preparar una base de datos aislada, la API y el MCP: el servidor
de GitHub no tiene nuestros servicios locales arrancados.

Utilizaremos datos de laboratorio, nunca el tablero de producción. Las claves
se mantendrán fuera del código y de los commits. Ejecutar evals con un modelo
real puede consumir llamadas al proveedor.

---

## 6. Descargar el código de la práctica

Repositorio público:

[jcarrenogallego/delivery-board](https://github.com/jcarrenogallego/delivery-board)

### Paso 1. Elegir una carpeta de trabajo

Abre una terminal en la carpeta donde quieras guardar la práctica.
Debe tener Git instalado y no existir ya una carpeta llamada `delivery-board`
en ese destino. Si existe, utiliza otra carpeta; no borres tu trabajo.

### Paso 2. Descargar el repositorio

```bash
git clone --branch main https://github.com/jcarrenogallego/delivery-board.git
cd delivery-board
```

Esto descarga el código de `main`. No necesitas permisos de escritura para
descargar un repositorio público.

### Paso 3. Localizar los componentes

```text
backend/     Aplicación .NET, API, MCP y orquestación con Aspire
frontend/    Interfaz de Delivery Board
agent/       Agente TypeScript con LangGraph
evals/       Documentación de la práctica
```

### Paso 4. Seguir el ejercicio

Abre la [guía práctica del eval](GUIA-EVAL-LANGSMITH-GITHUB.md) y comienza por
la **Parte 1: crear el eval y verlo en consola**.

Primero ejecutaremos la aplicación con Aspire y trabajaremos en local.
Todavía no hace falta crear Docker Compose ni configurar GitHub Actions.

**Descargar el repositorio no instala dependencias ni arranca los servicios.**
La guía indica cuándo preparar cada pieza. El código público no incluye tus
claves ni la configuración privada del entorno.
