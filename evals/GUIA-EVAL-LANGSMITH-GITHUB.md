# Un único eval, paso a paso: consola → LangSmith → CI

## Dinámica de la sesión

Vamos a construir el ejercicio progresivamente, **modificando el mismo fichero
`agent/scripts/eval-total.ts`**. No empezaremos con todo preparado:

1. **Local:** crear un eval pequeño, ejecutar el agente que se conecta a tu
   aplicación levantada con Aspire y ver una puntuación en consola.
2. **LangSmith:** después de que funcione, añadir el SDK y la configuración
   mínima, adaptar ese mismo fichero y ver el experimento en la web.
3. **GitHub Actions:** solo entonces crear Docker Compose, las fixtures y el
   workflow para levantar las dependencias en el servidor de CI.

En cada fase encontrarás el código completo y los pasos para ejecutarlo.

Todos los comandos parten de la raíz del monorepo `delivery-board`, salvo que
se indique entrar en `agent/`.

## El caso que vamos a evaluar

Para no depender del número total de tareas que tenga tu tablero, utilizaremos
un responsable exclusivo de laboratorio: **`EvalSesion`**, con **tres tareas**.

- Pregunta: cuántas tareas tiene ese responsable.
- Resultado esperado: `3`.
- Puntuación: `1` si acierta y `0` si no.

Este es un eval de **resultado**, no de uso de herramientas. Acertar el número
no demuestra por sí solo que el SQL o el recorrido del agente sean correctos.
Inspeccionar el recorrido en las trazas es una actividad complementaria.

---

# Parte 1 · Crear el eval y verlo en consola

**En esta parte no instalamos ni configuramos el SDK de LangSmith para el eval.
Tampoco creamos un Docker Compose nuevo.** Usaremos tu aplicación local de
Delivery Board y sus servicios, que levantarás con Aspire como habitualmente.

## 1. Instalar las dependencias habituales del agente

Necesitas Node.js 24 o posterior. En la raíz del repositorio:

```powershell
cd agent
npm ci
```

No ejecutes todavía `npm install langsmith`: lo haremos en la parte 2.

## 2. Crear `agent/scripts/eval-total.ts`: primera versión

Crea el fichero con este contenido:

```ts
import { awaitAllCallbacks } from "@langchain/core/callbacks/promises";
import { crearRuntime } from "../src/runtime.ts";
import { crearSesion } from "../src/sesion.ts";

const pregunta = "¿Cuántas tareas tiene el responsable EvalSesion? Responde únicamente con el número entero, sin texto adicional.";
const runtime = await crearRuntime();

try {
  const sesion = crearSesion(runtime.agente, runtime.config);
  const { resultado } = await sesion.enviar(pregunta);
  const respuesta = String(resultado.messages.at(-1)?.content ?? "").trim();
  const total = Number(respuesta);
  const puntuacion = total === 3 ? 1 : 0;

  console.log("Respuesta del agente:", respuesta);
  console.log("Total esperado:", 3);
  console.log("Puntuación:", puntuacion);
  console.log(puntuacion === 1 ? "Eval aprobado" : "Eval NO aprobado");
  if (puntuacion === 0) process.exitCode = 1;
} finally {
  try { await runtime.close(); }
  finally { await awaitAllCallbacks(); }
}
```

### Cómo leerlo

1. Crear el runtime y la sesión de nuestro agente.
2. Enviar la pregunta.
3. Convertir su respuesta a número.
4. Comparar con `3` y mostrar la puntuación con `console.log`.

El `finally` cierra las conexiones y espera los callbacks pendientes. Si no
aprueba, `process.exitCode = 1` permite que un futuro pipeline detecte el fallo.
No hay imports del SDK de LangSmith, datasets online, selectores de modo ni
código que borre o reescriba variables de entorno.

## 3. Levantar Delivery Board con Aspire y reutilizar la configuración local

Arranca la aplicación con tu dinámica habitual de Aspire y espera a que la API,
la base de datos y el MCP estén disponibles. Puede usar Docker para su base de
datos habitual; **aquí no añadimos un Compose específico del eval**.

Reutiliza el `agent/.env` con el que ya ejecutas el agente. Si aún no existe,
cópialo desde `.env.example` y configura tu modelo y tus propias credenciales.
Utiliza tus propias credenciales y no las incluyas en archivos versionados.

Aspire no exporta automáticamente la configuración a cualquier terminal Node
que abras por separado. Comprueba que estas direcciones del `.env` coinciden
con las que muestra su panel:

```dotenv
DELIVERY_API_URL=http://localhost:5202
MCP_URL=http://127.0.0.1:5091/mcp
```

Son ejemplos de los puertos habituales, no valores que debas imponer a Aspire.
Si tu API tiene otro puerto, utiliza el real. El MCP debe apuntar a `/mcp`.

No añadas claves de LangSmith ni cambies variables desde el script en esta fase.
Las trazas habituales del agente son independientes: este primer eval no crea
un experimento de evaluación.

## 4. Preparar y comprobar los datos locales

En la interfaz de Delivery Board, crea estas tareas de laboratorio **solo si
todavía no existen**:

| Título | Responsable | Prioridad |
| --- | --- | --- |
| Laboratorio A | EvalSesion | Medium |
| Laboratorio B | EvalSesion | Medium |
| Laboratorio C | EvalSesion | High |

No borres ni reasignes tareas ajenas para conseguir el resultado. El resto del
tablero puede tener cualquier número de tareas: solo contamos las de EvalSesion.
Antes de repetir, comprueba que no duplicas las tres tareas. Si ese responsable
ya se utiliza para otros datos, reserva otro nombre y cámbialo coherentemente
en la pregunta, el dataset y las fixtures de CI que prepararemos más adelante.

Puedes comprobar los datos sin preguntarle al modelo. Desde otra terminal,
usando la dirección real de la API:

```powershell
$api = "http://localhost:5202"
$tablero = Invoke-RestMethod "$api/api/work-items/dashboard"
$tareas = @($tablero.items | Where-Object { $_.owner -eq "EvalSesion" })
$tareas | Select-Object title, owner
$tareas.Count
```

Debe mostrar `3`. Esta lectura comprueba la preparación de datos; no es otro
eval ni modifica la base de datos.

## 5. Ejecutar la primera versión

Desde `agent/`, con Aspire todavía en marcha:

```powershell
node --env-file-if-exists=.env scripts/eval-total.ts
```

Resultado esperado:

```text
Respuesta del agente: 3
Total esperado: 3
Puntuación: 1
Eval aprobado
```

Si responde otro total, o una frase que no se puede convertir a número, la nota
será `0`. No cambies la referencia para ocultar un fallo. Primero revisa las
tres tareas, la configuración y la respuesta del agente.

**Pausa aquí hasta que funcione.** Todavía no hemos necesitado una cuenta de
LangSmith para evaluar el resultado. Las llamadas al modelo sí pueden consumir
tu proveedor, aunque solo estés mostrando el resultado en consola.

---

# Parte 2 · Añadir LangSmith al mismo fichero

Ahora que hemos entendido y ejecutado el eval local, vamos a guardar su caso,
traza y puntuación en LangSmith. **No creamos otro script ni otra infraestructura.**

## 6. Instalar ahora el SDK

Desde `agent/`:

```powershell
npm install --save-exact langsmith@0.11.8
```

Este paso actualizará `agent/package.json` y `agent/package-lock.json`. Ambos
deben versionarse cuando prepares la entrega, pero nunca el `.env`.

## 7. Crear el dataset y añadir el ejemplo en la web

### 7.1. Crear el dataset

En el workspace correcto de LangSmith:

1. Abre **Datasets & Experiments** y pulsa **+ New Dataset**.
2. Selecciona **Create from scratch**.
3. En **Name**, escribe exactamente `delivery-board-total-3`. Es obligatorio:
   sin nombre no se habilita el botón **Create**.
4. **Description** es opcional. En **Agent**, selecciona `delivery-board`.
5. Deja **Tags** y **Dataset schema** sin configurar para este ejercicio.
   No necesitas **Apply chat model schema** ni transformaciones.
6. Pulsa **Create**, arriba a la derecha.

**Input schema / Output schema definen la estructura de los datos, no el caso
que vamos a evaluar.** El ejemplo se añade después de crear el dataset.
Si decides definir un esquema, sus campos deben llamarse `pregunta` (string)
y `total` (integer), respectivamente.

### 7.2. Abrir el formulario del ejemplo

En la página del dataset `delivery-board-total-3`:

1. Abre la pestaña **Examples**.
2. Pulsa **+ Example**, a la derecha.
3. Se abrirá **Add Example to dataset**.

### 7.3. Rellenar Inputs y Reference Outputs

1. Deja **Dataset Splits** con el valor **base**.
2. En **Inputs**, deja seleccionado **YAML** en el desplegable inferior.
   Sustituye el contenido de ejemplo por:

```yaml
pregunta: "¿Cuántas tareas tiene el responsable EvalSesion? Responde únicamente con el número entero, sin texto adicional."
```

3. En **Reference Outputs**, deja también **YAML** y sustituye el contenido por:

```yaml
total: 3
```

El `3` va **sin comillas**, porque es un número. La pregunta es texto y debe
coincidir exactamente con la que contiene el script. YAML y JSON representan
los mismos campos; no necesitas cambiar el editor a JSON ni añadir llaves
cuando estás usando YAML.

4. No necesitas **Attachments**, **Upload Files** ni **Metadata**.
5. Pulsa **Submit**, arriba a la derecha, para guardar el ejemplo.

### 7.4. Comprobar el resultado

Vuelve a **Examples** y comprueba que hay **una sola fila**, con la pregunta
y el total de referencia `3`. Ese valor corresponde a las tres tareas de
`EvalSesion` que preparaste en la aplicación local.

Hasta aquí solo hemos guardado el caso: todavía no se ha ejecutado el agente
ni creado un experimento. La ejecución llegará al lanzar el script adaptado.

## 8. Añadir únicamente la configuración necesaria

En el `.env` del agente, **conserva** la clave del modelo y las direcciones
locales de API/MCP que ya funcionan. Añade o actualiza:

```dotenv
LANGSMITH_API_KEY=PEGA_AQUI_TU_CLAVE_DE_LANGSMITH
LANGSMITH_TRACING=true
LANGCHAIN_TRACING_V2=true
```

El endpoint solo se añade si necesitas indicar tu región explícitamente:

```dotenv
# Región US
LANGSMITH_ENDPOINT=https://api.smith.langchain.com
# Si tu workspace es EU, sustituye la línea anterior por:
# LANGSMITH_ENDPOINT=https://eu.api.smith.langchain.com
```

Si tu clave exige seleccionar workspace, añade `LANGSMITH_WORKSPACE_ID` con su
identificador. Si no, no lo necesitas.

### Configuración del experimento

- En este proyecto `prepararTelemetria()` establece valores por defecto para
  ambos flags. Déjalos a `true` en esta fase para mantenerlos coherentes; no
  añadas lógica de configuración al script del eval.
- Este experimento no necesita `LANGSMITH_PROJECT`, `LANGCHAIN_PROJECT`,
  `LANGSMITH_AGENT_ID` ni `LANGSMITH_AGENT_ENVIRONMENT`: deja esas entradas
  sin configurar para este ejercicio.

Todo esto se hace **en la configuración**, no mediante `delete process.env`
ni asignaciones dentro de `eval-total.ts`.

Para inspeccionar las entradas y salidas ficticias de este ejercicio, configura:

```dotenv
LANGSMITH_HIDE_INPUTS=false
LANGSMITH_HIDE_OUTPUTS=false
```

No utilices datos personales ni secretos en el dataset. No pegues claves en
el chat, en el código ni en GitHub. Reinicia el comando tras cambiar el `.env`.

## 9. Adaptar el mismo `eval-total.ts`

Estos son los cambios respecto a la primera versión:

1. **Añadir** `import { evaluate } from "langsmith/evaluation"`.
2. **Mover** la ejecución del agente a `ejecutarAgente`, que recibirá el input
   del dataset y devolverá la respuesta y el total.
3. **Extraer** la comparación a `evaluarTotal`, que devuelve `{ key, score }`.
4. **Sustituir** la ejecución manual de un caso por `await evaluate(...)`.
5. Mantener el cierre y añadir la comprobación de una única puntuación aprobada.

Para no mezclar fragmentos, sustituye el contenido del **mismo fichero** por
esta segunda versión completa:

```ts
import { evaluate } from "langsmith/evaluation";
import { awaitAllCallbacks } from "@langchain/core/callbacks/promises";
import { crearRuntime } from "../src/runtime.ts";
import { crearSesion } from "../src/sesion.ts";

type Datos = Record<string, unknown>;
const pregunta = "¿Cuántas tareas tiene el responsable EvalSesion? Responde únicamente con el número entero, sin texto adicional.";
const notas: number[] = [];
const runtime = await crearRuntime();

// 1. Ejecutar el agente para el ejemplo del dataset.
async function ejecutarAgente(inputs: Datos) {
  if (inputs.pregunta !== pregunta) throw new Error("Pregunta inesperada en el dataset");
  const sesion = crearSesion(runtime.agente, runtime.config);
  const { resultado } = await sesion.enviar(pregunta);
  const respuesta = String(resultado.messages.at(-1)?.content ?? "").trim();
  return { respuesta, total: Number(respuesta) };
}

// 2. La comparación que antes hacíamos directamente en consola.
function evaluarTotal({ outputs, referenceOutputs }: {
  outputs?: Datos;
  referenceOutputs?: Datos;
}) {
  const correcto = outputs?.total === 3 && referenceOutputs?.total === 3;
  const nota = correcto ? 1 : 0;
  notas.push(nota);
  console.log("Total obtenido:", outputs?.total, "Puntuación:", nota);
  return { key: "total_correcto", score: nota };
}

// 3. LangSmith ejecuta el ejemplo y guarda el experimento y su puntuación.
try {
  await evaluate(ejecutarAgente, {
    data: "delivery-board-total-3",
    evaluators: [evaluarTotal],
    experimentPrefix: "delivery-board-total",
    maxConcurrency: 1,
    numRepetitions: 1,
  });

  const aprobado = notas.length === 1 && notas[0] === 1;
  console.log(aprobado ? "Eval aprobado" : "Eval NO aprobado");
  if (!aprobado) process.exitCode = 1;
} finally {
  try { await runtime.close(); }
  finally { await awaitAllCallbacks(); }
}
```

La regla sigue siendo la misma: el total debe ser `3`. `evaluate()` toma el
ejemplo del dataset, llama al agente y al evaluador y registra el experimento.
El `console.log` del evaluador permite seguir viendo la puntuación en la terminal.

No hay dos modos dentro del fichero: la primera versión era solo local; esta
segunda añade LangSmith y será la que reutilizaremos en CI.

## 10. Crear el evaluador reutilizable en la web (opcional)

La versión del SDK ya envía `total_correcto` al experimento. **No tienes que
crear un evaluador web para ver esa puntuación.** Guardar código en el script
no crea automáticamente una definición reutilizable en el catálogo Evaluators.

Si quieres mostrar también esa pantalla durante la sesión:

1. Abre **Evaluators → + Evaluator → Code evaluator / Custom code**.
2. Nombre: **Delivery Board - total correcto**.
3. Selecciona Python en el editor y pega:

```python
def perform_eval(run, example):
    actual = run.get("outputs") or {}
    esperado = (example or {}).get("outputs") or {}
    correcto = actual.get("total") == 3 and esperado.get("total") == 3
    return {"total_correcto": 1 if correcto else 0}
```

4. En el editor, un run con `outputs: {"total": 3}` y un example con
   `outputs: {"total": 3}` deben puntuar `1`. Si el run devuelve `2`, puntúa `0`.
5. Guarda la definición.

No actives además una regla automática con ese evaluador sobre el dataset
durante este ejercicio: el script ya ejecuta la misma regla y la duplicarías.
No necesitamos un modelo juez ni una clave OpenAI en el editor web.

## 11. Ejecutar de nuevo y ver qué sucede online

Con los mismos servicios locales, las mismas tres tareas y el `.env` actualizado,
ejecuta otra vez desde `agent/`:

```powershell
node --env-file-if-exists=.env scripts/eval-total.ts
```

Ahora, además de la salida en consola, abre en LangSmith:

**Datasets & Experiments → delivery-board-total-3 → nuevo experimento con
prefijo `delivery-board-total`.**

Revisa la pregunta, la referencia `3`, la salida `respuesta/total`, el feedback
`total_correcto` y la traza del agente.

La ejecución sigue ocurriendo en tu equipo. La web no levanta tu API ni ejecuta
tu aplicación por el hecho de crear un dataset.

**Pausa aquí hasta comprobar el experimento. Solo después pasamos al pipeline.**

---

# Parte 3 · Preparar el entorno y el pipeline de CI

## 12. Qué cambia al pasar al servidor de GitHub

El runner no tiene tu Aspire arrancado, tu base local ni tu `.env`. Por eso
**ahora sí** crearemos la infraestructura específica de evaluación:

```text
Runner Ubuntu de GitHub Actions
  PostgreSQL temporal con Docker Compose
  API .NET → migraciones y lector MCP
  MCP .NET de solo lectura
  El mismo eval-total.ts de la parte 2 → OpenAI y LangSmith
```

El MCP exige loopback: Compose levantará la base de datos y el workflow
arrancará API/MCP en el propio runner, junto al agente. No abriremos el MCP a
la red ni necesitaremos el frontend, Aspire o el asistente .NET antiguo en CI.

El fichero del eval no vuelve a cambiar. Solo cambia la configuración que
recibe del entorno:

| Dato | En local | En CI |
| --- | --- | --- |
| API | Dirección real del panel de Aspire | `http://127.0.0.1:55202` |
| MCP | Dirección real de Aspire con `/mcp` | `http://127.0.0.1:55091/mcp` |
| PostgreSQL | Base habitual de formación | Base temporal `deliveryboard_eval`, puerto 55439 |
| Claves | Configuración privada del agente | GitHub Secrets |
| LangSmith | Dataset creado en la parte 2 | El mismo dataset y otro experimento |

## 13. Crear ahora `evals/compose.eval.yml`

Desde la raíz del monorepo, crea este fichero:

```yaml
name: delivery-board-eval
services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: solo-laboratorio
      POSTGRES_DB: deliveryboard_eval
    ports:
      - "127.0.0.1:55439:5432"
    tmpfs:
      - /var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d deliveryboard_eval"]
      interval: 2s
      timeout: 3s
      retries: 40
```

La contraseña es solo de laboratorio y el puerto solo se publica en loopback.
La base usa tmpfs: no comparte ni conserva volúmenes del tablero habitual.
No hay que subir una base de datos de tu ordenador al repositorio.

No ejecutes este Compose en la primera parte de la sesión; se utilizará en el
workflow. Si decides ensayarlo después en local, sus puertos son distintos de
los servicios habituales y sus datos deben permanecer aislados.

## 14. Crear `evals/seed.sql`: los mismos datos, en una base nueva

En CI no existen las tareas que creaste mediante la interfaz. Este seed creará
las tres tareas de **EvalSesion** tras las migraciones de la API:

```sql
BEGIN;
DO $guard$
BEGIN
  IF current_database() <> 'deliveryboard_eval' THEN
    RAISE EXCEPTION 'Solo se permite sembrar deliveryboard_eval';
  END IF;
  IF EXISTS (SELECT 1 FROM public.work_items) THEN
    RAISE EXCEPTION 'La base no esta vacia. No se borraran datos existentes';
  END IF;
END;
$guard$;

INSERT INTO public.work_items
  ("Id", "Title", "Owner", "Priority", "Status", "CreatedAt", "PrerequisiteIds", "Comment")
VALUES
  ('00000000-0000-4000-8000-000000000001', 'Laboratorio A', 'EvalSesion', 'Medium', 'Pending', '2026-01-01T00:00:00Z', ARRAY[]::uuid[], NULL),
  ('00000000-0000-4000-8000-000000000002', 'Laboratorio B', 'EvalSesion', 'Medium', 'Pending', '2026-01-01T00:00:00Z', ARRAY[]::uuid[], NULL),
  ('00000000-0000-4000-8000-000000000003', 'Laboratorio C', 'EvalSesion', 'High', 'Pending', '2026-01-01T00:00:00Z', ARRAY[]::uuid[], NULL);
COMMIT;
```

El seed rechaza una base distinta o no vacía; no borra datos para forzar la
puntuación. Se ejecuta una vez en la base temporal y no en tu PostgreSQL habitual.

## 15. Configurar Secrets y Variables de GitHub

En **delivery-board → Settings → Secrets and variables → Actions**:

### Secrets

| Nombre | Contenido |
| --- | --- |
| `OPENAI_API_KEY` | Clave real del modelo para el laboratorio. |
| `LANGSMITH_API_KEY` | Clave del workspace donde existe el dataset. |

### Variables

| Nombre | Contenido |
| --- | --- |
| `LANGSMITH_ENDPOINT` | Endpoint de la región del workspace. |
| `LANGSMITH_WORKSPACE_ID` | Solo si la clave necesita seleccionar workspace. |

Configura claves reales en los secretos: los valores de marcador no sirven
para ejecutar CI.
El pipeline no puede leer tus archivos `.env` locales; no los subas.

## 16. Crear `.github/workflows/eval-total.yml`

Este es el contenido completo para copiar al fichero real. Tenerlo escrito
dentro del Markdown no activa GitHub Actions.

El ejemplo se dispara por push a **`codex/validar-eval-ci`**, sin abrir PR,
para validar la rama antes de adaptar la política definitiva de `main`.

```yaml
name: Eval Delivery Board - total

on:
  push:
    branches: [codex/validar-eval-ci]
  workflow_dispatch:

permissions:
  contents: read

jobs:
  eval-total:
    name: eval-total
    if: >-
      github.repository == 'jcarrenogallego/delivery-board' &&
      github.actor == 'jcarrenogallego'
    runs-on: ubuntu-latest
    timeout-minutes: 20
    env:
      ASPNETCORE_ENVIRONMENT: Evaluation
      DOTNET_NOLOGO: 'true'
      DOTNET_CLI_TELEMETRY_OPTOUT: 'true'
      McpReader__Password: lector-exclusivo-del-eval
    steps:
      - uses: actions/checkout@v7
        with:
          persist-credentials: false

      - uses: actions/setup-node@v7
        with:
          node-version: '24'
          package-manager-cache: false

      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: '10.0.x'

      - name: Instalar agente con el lockfile
        working-directory: agent
        run: npm ci --ignore-scripts

      - name: Comprobar TypeScript
        working-directory: agent
        run: npm run check

      - name: Compilar solo API y MCP
        run: |
          dotnet build backend/src/DeliveryBoard.Api/DeliveryBoard.Api.csproj -c Release --nologo
          dotnet build backend/src/DeliveryBoard.Mcp/DeliveryBoard.Mcp.csproj -c Release --nologo

      - name: Arrancar base temporal
        run: docker compose -f evals/compose.eval.yml up -d --wait

      - name: Arrancar API, migrar y sembrar fixtures
        env:
          ConnectionStrings__deliveryboard: Host=127.0.0.1;Port=55439;Database=deliveryboard_eval;Username=postgres;Password=solo-laboratorio
        shell: bash
        run: |
          set -euo pipefail
          mkdir -p .eval-logs
          nohup dotnet run --project backend/src/DeliveryBoard.Api/DeliveryBoard.Api.csproj \
            --configuration Release --no-build --no-launch-profile \
            --urls http://127.0.0.1:55202 > .eval-logs/api.log 2>&1 &
          echo $! > .eval-logs/api.pid
          ready=false
          for i in $(seq 1 90); do
            if curl -fsS http://127.0.0.1:55202/health >/dev/null 2>&1; then ready=true; break; fi
            sleep 2
          done
          if [ "$ready" != true ]; then echo "API no disponible"; exit 1; fi
          cat evals/seed.sql | docker compose -f evals/compose.eval.yml exec -T postgres \
            psql -v ON_ERROR_STOP=1 -U postgres -d deliveryboard_eval

      - name: Arrancar MCP de solo lectura
        env:
          ConnectionStrings__deliveryboard: Host=127.0.0.1;Port=55439;Database=deliveryboard_eval;Username=mcp_reader
        shell: bash
        run: |
          set -euo pipefail
          nohup dotnet run --project backend/src/DeliveryBoard.Mcp/DeliveryBoard.Mcp.csproj \
            --configuration Release --no-build --no-launch-profile \
            --urls http://127.0.0.1:55091 > .eval-logs/mcp.log 2>&1 &
          echo $! > .eval-logs/mcp.pid
          ready=false
          for i in $(seq 1 90); do
            if curl -fsS http://127.0.0.1:55091/health >/dev/null 2>&1; then ready=true; break; fi
            sleep 2
          done
          if [ "$ready" != true ]; then echo "MCP no disponible"; exit 1; fi

      - name: Ejecutar el único eval y aplicar el criterio de aprobación
        working-directory: agent
        env:
          MODEL_PROVIDER: openai
          OPENAI_MODEL: gpt-4o-mini
          OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
          DELIVERY_API_URL: http://127.0.0.1:55202
          MCP_URL: http://127.0.0.1:55091/mcp
          SKILLS_MODE: inline
          MAX_SQL_ATTEMPTS: '2'
          MAX_MODEL_CALLS: '12'
          REQUEST_TIMEOUT_MS: '120000'
          LANGSMITH_API_KEY: ${{ secrets.LANGSMITH_API_KEY }}
          LANGSMITH_ENDPOINT: ${{ vars.LANGSMITH_ENDPOINT }}
          LANGSMITH_WORKSPACE_ID: ${{ vars.LANGSMITH_WORKSPACE_ID }}
          LANGSMITH_TRACING: 'true'
          LANGCHAIN_TRACING_V2: 'true'
          LANGSMITH_HIDE_INPUTS: 'false'
          LANGSMITH_HIDE_OUTPUTS: 'false'
        shell: bash
        run: |
          set -euo pipefail
          test -n "$OPENAI_API_KEY" || { echo "Falta OPENAI_API_KEY"; exit 1; }
          test -n "$LANGSMITH_API_KEY" || { echo "Falta LANGSMITH_API_KEY"; exit 1; }
          test -n "$LANGSMITH_ENDPOINT" || { echo "Falta LANGSMITH_ENDPOINT"; exit 1; }
          if [ -z "$LANGSMITH_WORKSPACE_ID" ]; then unset LANGSMITH_WORKSPACE_ID; fi
          node scripts/eval-total.ts

      - name: Mostrar diagnóstico si falla
        if: failure()
        shell: bash
        run: |
          for file in .eval-logs/api.log .eval-logs/mcp.log; do
            if [ -f "$file" ]; then echo "--- $file ---"; tail -n 80 "$file"; fi
          done

      - name: Limpiar exclusivamente el entorno del eval
        if: always()
        shell: bash
        run: |
          for file in .eval-logs/api.pid .eval-logs/mcp.pid; do
            if [ -f "$file" ]; then kill "$(cat "$file")" 2>/dev/null || true; fi
          done
          docker compose -f evals/compose.eval.yml down || true
```

El workflow instala dependencias, comprueba TypeScript, compila API/MCP,
arranca PostgreSQL, espera la API, siembra los datos, espera el MCP y ejecuta
el mismo fichero de la parte 2. Al finalizar limpia exclusivamente su entorno.

Aquí sí se proporcionan las variables específicas de CI. No se borran ni se
sobrescriben desde el script del eval. Las claves solo se entregan al paso que
ejecuta la evaluación, no a la instalación o a la compilación.

Los bloques de comandos del runner son Bash; no copies los comandos PowerShell
locales dentro de este YAML. Para un entorno más estricto fija también las
acciones e imágenes por SHA/digest y revisa periódicamente las versiones.

## 17. Guardar y ejecutar el workflow

Los ficheros que deberán estar versionados son:

```text
agent/scripts/eval-total.ts       # La segunda versión, con evaluate()
agent/package.json
agent/package-lock.json
evals/compose.eval.yml
evals/seed.sql
.github/workflows/eval-total.yml
```

Añade `/.eval-logs/` al `.gitignore` de la raíz. **Nunca añadas `.env` ni claves.**
Conserva los hooks de Git existentes; pueden compilar y probar al hacer commit.

Cuando hayas revisado todo y decidas subir la rama:

1. El push a `codex/validar-eval-ci` disparará el workflow sin PR.
2. Abre **Actions → Eval Delivery Board - total → job eval-total**.
3. Comprueba cada paso y después el nuevo experimento en LangSmith.

`workflow_dispatch` se conserva, pero el botón manual puede no estar disponible
hasta que el workflow exista en la rama predeterminada. La primera validación
de esta rama utiliza el push, no depende de ese botón.

| Resultado | Significado |
| --- | --- |
| Feedback `1` y job verde | El único caso ha pasado. |
| Feedback `0` y job rojo | El resultado no cumple la referencia. |
| Error antes del eval | Revisar preparación, compilación, servicios o credenciales; puede no existir experimento. |

El script exige una única nota aprobada y usa código de salida de error si no
se cumple. Guardar una nota en LangSmith por sí solo no bloquea GitHub.

No exijas todavía este check como protección de `main`: no se ejecuta en esa
rama ni en PRs. Una vez validado, adapta los eventos y la protección a la
política definitiva. Este ejercicio cubre CI; no despliega la aplicación.

---

## Repositorio público y contribuciones del equipo

Mantén el repositorio público y sin otros colaboradores con escritura. El
equipo puede leer, clonar, crear forks y proponer cambios; solo tú decides
qué fusionar en el repositorio original.

El workflow tiene `contents: read` y filtros de repositorio y propietario.
No utiliza eventos PR ni `pull_request_target`; los forks no disparan este
workflow con tus claves. Para evaluar cambios externos, revisa antes TODO el
código, dependencias y workflows y pásalos de forma controlada a una rama de
confianza. No compartas claves ni tokens de escritura.

Quien pruebe su propio fork tendrá que ajustar el filtro de usuario/repositorio
y usar su dataset y sus secretos. El fork no concede escritura sobre el original.

## Qué hemos hecho en cada fase

| Fase | Creamos o cambiamos | Dónde se ejecuta |
| --- | --- | --- |
| 1 | Script pequeño y datos de laboratorio en la app existente | Consola local, servicios de Aspire |
| 2 | SDK, dataset, configuración mínima y segunda versión del mismo script | Equipo local; resultados en LangSmith |
| 3 | Compose, seed, Secrets/Variables y workflow | Runner temporal de GitHub |

Las salidas mostradas ilustran el resultado esperado del ejercicio.
Cada ejecución del agente puede consumir el proveedor; LangSmith tiene sus
propios límites o costes de uso.

## Referencias

- [Evaluaciones en LangSmith](https://docs.langchain.com/langsmith/evaluation-quickstart).
- [Evaluadores del SDK](https://docs.langchain.com/langsmith/code-evaluator-sdk).
- [Evaluadores web](https://docs.langchain.com/langsmith/code-evaluator-ui).
- [Secrets de GitHub Actions](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets).
- [Permisos de repositorios personales](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/repository-access-and-collaboration/permission-levels-for-a-personal-account-repository).
