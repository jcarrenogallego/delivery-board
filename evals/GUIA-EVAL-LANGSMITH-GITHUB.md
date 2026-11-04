# Un único eval: local → LangSmith → GitHub Actions

## Objetivo

Evaluar **nuestro Delivery Board Agent real**, con OpenAI, PostgreSQL y MCP:

> Con tres tareas de laboratorio, preguntar cuántas hay. El agente debe obtener el esquema, consultar SQL y responder `3`.

Usaremos **un caso y un criterio**, no una batería de pruebas. La puntuación será `1` si cumple todo lo anterior y `0` si no. Un fallo debe hacer fallar también el pipeline.

Este Markdown contiene todos los ficheros para copiar y pegar. **Todavía no están implementados ni se ha ejecutado el ejemplo.** Publicar este documento no activa GitHub Actions.

Todos los comandos parten de la raíz del monorepo `delivery-board`, salvo donde indicamos entrar en `agent/`. No trabajes en el repositorio histórico `proyecto-codex-dotnet`.

## Qué vamos a levantar

```text
PostgreSQL temporal (55439, base deliveryboard_eval)
        ↑
API .NET (55202): aplica migraciones y aprovisiona mcp_reader
        ↑
MCP .NET (55091): consulta con el usuario de solo lectura
        ↑
Agente TypeScript → OpenAI
        ↓
Evaluador en código → consola / experimento en LangSmith
```

No necesitamos el frontend, Aspire ni el asistente .NET antiguo. **El MCP exige conexiones loopback**; lo ejecutamos en el mismo equipo/runner que el agente, no detrás de una red Docker que obligue a abrirlo a otros hosts. Solo PostgreSQL va en Docker.

## Ficheros que crearás siguiendo la guía

```text
evals/compose.eval.yml
evals/seed.sql
agent/.env.eval                 # Privado, ignorado por Git
agent/scripts/eval-total.ts
.github/workflows/eval-total.yml
```

También actualizarás `agent/package.json` y su lockfile al instalar el SDK. Esta guía usa la variante actual de `createAgent`, no la variante opcional Deep Agents.

---

# Parte 1 · Ejecutar el eval en local

## 1. Requisitos

- Docker con Compose v2 y soporte de `up --wait`.
- .NET SDK 10.
- Node.js 24 o posterior, con ejecución nativa de TypeScript.
- Una clave válida de OpenAI. **Revoca la clave antigua que se encontró en el historial; no la reutilices.**
- Puertos `55439`, `55202` y `55091` libres.

Usaremos una base nueva y aislada. No conectes el seed a tu tablero habitual. Los comandos de arranque .NET compilan los proyectos cuando los ejecutes; preparar o leer este Markdown no compila nada.

## 2. Crear `evals/compose.eval.yml`

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

La contraseña anterior es **solo para esta base temporal**, no es una credencial de producción. No hay un volumen persistente: al eliminar este contenedor se pierden sus fixtures. No cambies el nombre del proyecto para apuntar a contenedores ajenos.

Desde la raíz, en una primera terminal:

```powershell
docker compose -f evals/compose.eval.yml up -d --wait
```

## 3. Arrancar la API en una segunda terminal

Abre PowerShell en la raíz del repositorio:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Evaluation"
$env:ConnectionStrings__deliveryboard = "Host=127.0.0.1;Port=55439;Database=deliveryboard_eval;Username=postgres;Password=solo-laboratorio"
$env:McpReader__Password = "lector-exclusivo-del-eval"
dotnet run --project backend/src/DeliveryBoard.Api/DeliveryBoard.Api.csproj --configuration Release --no-launch-profile --urls http://127.0.0.1:55202
```

Déjala abierta. La API existente aplica las migraciones y crea el lector MCP antes de escuchar peticiones. No hay que ejecutar `dotnet ef` por separado.

En otra terminal puedes comprobar su disponibilidad:

```powershell
Invoke-RestMethod http://127.0.0.1:55202/health
```

Espera a que responda `status: ok` antes de continuar.

## 4. Crear las tres tareas de laboratorio

Crea `evals/seed.sql`:

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
  ('00000000-0000-4000-8000-000000000001', 'Laboratorio A', 'Ana', 'Medium', 'Pending', '2026-01-01T00:00:00Z', ARRAY[]::uuid[], NULL),
  ('00000000-0000-4000-8000-000000000002', 'Laboratorio B', 'Luis', 'Medium', 'Pending', '2026-01-01T00:00:00Z', ARRAY[]::uuid[], NULL),
  ('00000000-0000-4000-8000-000000000003', 'Laboratorio C', 'Ana', 'High', 'Pending', '2026-01-01T00:00:00Z', ARRAY[]::uuid[], NULL);
COMMIT;
```

Desde la raíz, con la API ya disponible:

```powershell
Get-Content -Raw evals/seed.sql | docker compose -f evals/compose.eval.yml exec -T postgres psql -v ON_ERROR_STOP=1 -U postgres -d deliveryboard_eval
```

Se utiliza SQL directo **solo para preparar las fixtures de esta base aislada**. El agente no recibe credenciales administrativas. El seed se ejecuta una vez; si lo repites, rechaza la base no vacía sin borrar nada.

## 5. Arrancar el MCP en otra terminal

Desde la raíz:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Evaluation"
$env:ConnectionStrings__deliveryboard = "Host=127.0.0.1;Port=55439;Database=deliveryboard_eval;Username=mcp_reader"
$env:McpReader__Password = "lector-exclusivo-del-eval"
dotnet run --project backend/src/DeliveryBoard.Mcp/DeliveryBoard.Mcp.csproj --configuration Release --no-launch-profile --urls http://127.0.0.1:55091
```

Déjala abierta. Desde otra terminal:

```powershell
Invoke-RestMethod http://127.0.0.1:55091/health
```

El lector se llama `mcp_reader`; **no sustituyas ese usuario por `postgres` en el MCP**.

## 6. Instalar el SDK de evaluación

En la terminal desde la que ejecutarás el agente:

```powershell
cd agent
npm ci
npm install --save-exact langsmith@0.11.8
```

El SDK se instala como dependencia directa porque el nuevo script lo importa. Guarda después en Git tanto `package.json` como `package-lock.json`; el pipeline utilizará `npm ci`, no instalará versiones nuevas por su cuenta.

## 7. Crear el archivo privado `agent/.env.eval`

Estando en `agent/`, crea `.env.eval`:

```dotenv
MODEL_PROVIDER=openai
OPENAI_MODEL=gpt-4o-mini
OPENAI_API_KEY=PEGA_AQUI_TU_NUEVA_CLAVE
DELIVERY_API_URL=http://127.0.0.1:55202
MCP_URL=http://127.0.0.1:55091/mcp
SKILLS_MODE=inline
MAX_SQL_ATTEMPTS=2
MAX_MODEL_CALLS=12
REQUEST_TIMEOUT_MS=120000

EVAL_DESTINO=local
LANGSMITH_TRACING=false
LANGCHAIN_TRACING_V2=false
```

No modifiques tu `.env` habitual. `.env.eval` está cubierto por el `.gitignore`; nunca debes subirlo. Sustituye el marcador de la clave, no lo dejes como texto literal.

## 8. Crear `agent/scripts/eval-total.ts`

```ts
import { evaluate } from "langsmith/evaluation";
import { isAIMessage, isToolMessage } from "@langchain/core/messages";
import { awaitAllCallbacks } from "@langchain/core/callbacks/promises";
import { crearRuntime } from "../src/runtime.ts";
import { crearSesion } from "../src/sesion.ts";

const PREGUNTA = "¿Cuántas tareas hay en total? Responde únicamente con el número entero, sin texto adicional.";
const destino = process.env.EVAL_DESTINO ?? "local";
if (!["local", "langsmith"].includes(destino)) {
  throw new Error("EVAL_DESTINO debe ser local o langsmith");
}

// Cada experimento crea su propio proyecto. No reutilizar el destino
// de las trazas normales de un agente registrado en el workspace.
delete process.env.LANGSMITH_AGENT_ID;
delete process.env.LANGSMITH_AGENT_ENVIRONMENT;
delete process.env.LANGSMITH_PROJECT;
delete process.env.LANGCHAIN_PROJECT;
process.env.LANGSMITH_TRACING = String(destino === "langsmith");
process.env.LANGCHAIN_TRACING_V2 = String(destino === "langsmith");

let evaluados = 0;
let fallos = 0;

type Datos = Record<string, unknown>;
function totalCorrecto({ outputs, referenceOutputs }: {
  outputs?: Datos;
  referenceOutputs?: Datos;
}) {
  const correcto = referenceOutputs?.total === 3 &&
    outputs?.total === 3 &&
    outputs?.usoEsquema === true && outputs?.usoSql === true;
  evaluados++;
  if (!correcto) fallos++;
  return { key: "total_correcto", score: correcto ? 1 : 0 };
}

const runtime = await crearRuntime();
try {
  async function ejecutar(inputs: Datos) {
    // El dataset de este ejercicio solo puede ejecutar esta pregunta.
    if (inputs.pregunta !== PREGUNTA) {
      throw new Error("El ejemplo no coincide con la pregunta del laboratorio");
    }
    const { resultado, pausas } = await crearSesion(
      runtime.agente, runtime.config,
    ).enviar(PREGUNTA);
    if (pausas.length) throw new Error("Una consulta de total no debe pedir confirmación");

    const uso = new Set<string>();
    for (const m of resultado.messages) {
      if (!isToolMessage(m) || typeof m.content !== "string") continue;
      try {
        const dato = JSON.parse(m.content);
        if (dato.ok === true && m.name) uso.add(m.name);
      } catch { /* Un contenido no JSON no cuenta como éxito de estas tools. */ }
    }
    const ultimo = resultado.messages.at(-1);
    const respuesta = ultimo && isAIMessage(ultimo) &&
      typeof ultimo.content === "string" ? ultimo.content.trim() : "";
    return {
      respuesta,
      total: /^\d+$/.test(respuesta) ? Number(respuesta) : null,
      usoEsquema: uso.has("obtener_esquema"),
      usoSql: uso.has("consultar_sql"),
    };
  }

  if (destino === "local") {
    const outputs = await ejecutar({ pregunta: PREGUNTA });
    const nota = totalCorrecto({ outputs, referenceOutputs: { total: 3 } });
    console.log("Resultado:", outputs);
    console.log("Evaluación:", nota);
  } else {
    await evaluate(ejecutar, {
      data: "delivery-board-total-3",
      evaluators: [totalCorrecto],
      experimentPrefix: "delivery-board-total",
      maxConcurrency: 1,
      numRepetitions: 1,
    });
  }

  console.log(`Casos evaluados: ${evaluados}. Fallos: ${fallos}.`);
  // Guardar feedback no basta: esta condición es la puerta de aprobación de CI.
  if (evaluados !== 1 || fallos !== 0) process.exitCode = 1;
} finally {
  try { await runtime.close(); }
  finally { await awaitAllCallbacks(); }
}
```

Es una evaluación de **resultado y uso exitoso de herramientas**, no una comparación literal de una frase larga. Pedimos un número para poder comprobarlo sin un segundo modelo evaluador. No demuestra que cualquier pregunta o cualquier SQL sean correctos: cubre únicamente este caso.

## 9. Ejecutar primero sin LangSmith

Desde `agent/`:

```powershell
node --env-file-if-exists=.env.eval scripts/eval-total.ts
```

Si todo funciona, verás una salida similar a esta —es un resultado esperado, no una ejecución realizada al redactar la guía—:

```text
Resultado: { respuesta: '3', total: 3, usoEsquema: true, usoSql: true }
Evaluación: { key: 'total_correcto', score: 1 }
Casos evaluados: 1. Fallos: 0.
```

Si responde otro número, no usa las herramientas o falla un servicio, no debe aprobar. No cambies el valor esperado para ocultar un fallo. **Esta ejecución consume OpenAI**, aunque no envíe nada a LangSmith.

---

# Parte 2 · Ver el mismo eval en LangSmith

## 10. Crear el dataset en la web

En LangSmith, dentro del workspace correcto:

1. Abre **Datasets & Experiments**.
2. Crea un dataset llamado exactamente **`delivery-board-total-3`**.
3. Añade **un solo ejemplo** con los siguientes inputs y reference outputs.
4. Guarda el ejemplo.

Inputs:

```json
{
  "pregunta": "¿Cuántas tareas hay en total? Responde únicamente con el número entero, sin texto adicional."
}
```

Reference outputs:

```json
{ "total": 3 }
```

No añadas más filas a este dataset durante el ejercicio: el script exige exactamente un caso. La referencia `3` solo es válida para nuestra base aislada con las fixtures anteriores.

## 11. Crear también el evaluador en la sección Evaluators

Esto permite aprender la pantalla que vimos en la sesión. **No necesitas crearlo en la web para que el script muestre sus puntuaciones:** `evaluate()` ya envía su feedback. Un evaluador escrito en un script no aparece automáticamente como una definición reutilizable del catálogo web.

Para guardar la misma regla como evaluador reutilizable:

1. Entra en **Evaluators → + Evaluator**; alternativamente, utiliza **+ Evaluator** desde el dataset.
2. Elige la opción de **Code evaluator / Custom code**, no `LLM-as-a-Judge`.
3. Nombre: **`Delivery Board - total correcto`**.
4. Selecciona **Python** en el editor y pega:

```python
def perform_eval(run, example):
    actual = run.get("outputs") or {}
    esperado = (example or {}).get("outputs") or {}
    correcto = (
        esperado.get("total") == 3
        and actual.get("total") == 3
        and actual.get("usoEsquema") is True
        and actual.get("usoSql") is True
    )
    return {"total_correcto": 1 if correcto else 0}
```

5. Para comprobarlo en el editor, usa un run con `outputs` igual a `{ "total": 3, "usoEsquema": true, "usoSql": true }` y un example con `outputs` igual a `{ "total": 3 }`. Debe puntuar `1`; cambia el total del run a `2` y debe puntuar `0`.
6. Guarda el evaluador. Los nombres concretos de los botones pueden variar entre versiones de la interfaz.

**En la ejecución que sigue usamos el evaluador del script para controlar CI.** No actives además una regla automática con el mismo evaluador web sobre el dataset: duplicaría la evaluación. La versión guardada en la web queda disponible para una aplicación o revisión manual posterior sobre runs con ese formato de salida; no sirve para cualquier nodo interno del grafo, que puede tener otro formato.

No se instala Python en nuestro agente por este paso: ese editor pertenece a LangSmith. Tampoco necesitamos configurar un modelo juez o una clave OpenAI en la web, porque la regla es código, no una llamada a otro modelo.

## 12. Conectar el script con LangSmith

En `agent/.env.eval`, cambia `EVAL_DESTINO` y añade lo siguiente:

```dotenv
EVAL_DESTINO=langsmith
LANGSMITH_API_KEY=PEGA_AQUI_TU_CLAVE_DE_LANGSMITH
LANGSMITH_ENDPOINT=https://api.smith.langchain.com
LANGSMITH_TRACING=true
LANGCHAIN_TRACING_V2=true
LANGSMITH_HIDE_INPUTS=false
LANGSMITH_HIDE_OUTPUTS=false
```

Utiliza el endpoint real de tu workspace. Si está en la región europea, utiliza `https://eu.api.smith.langchain.com`. Si tu clave necesita seleccionar workspace, añade `LANGSMITH_WORKSPACE_ID` con su identificador.

No añadas aquí `LANGSMITH_AGENT_ID`, `LANGSMITH_AGENT_ENVIRONMENT` o `LANGSMITH_PROJECT`: este ejemplo crea experimentos, separados del destino de las trazas normales. El script elimina esas variables heredadas solo dentro de su proceso, sin modificar tu configuración habitual.

Entradas y salidas se registran porque **son datos ficticios del laboratorio** y necesitamos inspeccionarlas. No uses datos personales ni secretos en el dataset. Estas opciones no sustituyen una revisión de privacidad ni recuperan datos que se ocultaron en ejecuciones anteriores.

Vuelve a ejecutar, manteniendo los servicios y las tres fixtures:

```powershell
node --env-file-if-exists=.env.eval scripts/eval-total.ts
```

## 13. Ver el experimento

En **Datasets & Experiments → delivery-board-total-3**, abre el nuevo experimento cuyo nombre empieza por **`delivery-board-total`**. Revisa:

- La pregunta y la referencia.
- La salida del agente: `respuesta`, `total`, `usoEsquema` y `usoSql`.
- La columna de feedback **`total_correcto`**.
- La traza con las llamadas al modelo y a las herramientas.

La evaluación se ejecuta en tu equipo; LangSmith almacena el experimento, las trazas y las puntuaciones. **Crear el dataset/evaluador en la web no arranca tu aplicación local.**

---

# Parte 3 · Llevar exactamente el mismo caso a GitHub Actions

## 14. Guardar los ficheros que necesita CI

Antes de activar el workflow, versiona los ficheros que has creado siguiendo esta guía:

```text
evals/compose.eval.yml
evals/seed.sql
agent/scripts/eval-total.ts
agent/package.json
agent/package-lock.json
```

**Nunca `agent/.env.eval`.** Comprueba el contenido preparado antes de hacer commit. Conserva los hooks existentes: pueden compilar y probar cuando implementes estos cambios. No se incluyen instrucciones para saltárselos.

Añade también `/.eval-logs/` al `.gitignore` de la raíz: esa carpeta del workflow contiene logs y PID temporales, no código para versionar.

El dataset debe existir ya en LangSmith. CI lo lee por su nombre; no lo recrea ni añade filas en cada push.

## 15. Crear secretos y variables en GitHub

En tu repositorio **delivery-board → Settings → Secrets and variables → Actions**:

### Secrets

| Nombre | Contenido |
| --- | --- |
| `OPENAI_API_KEY` | Nueva clave del proveedor para este laboratorio. |
| `LANGSMITH_API_KEY` | Clave del workspace donde está el dataset. |

### Variables

| Nombre | Contenido |
| --- | --- |
| `LANGSMITH_ENDPOINT` | El endpoint de tu región. |
| `LANGSMITH_WORKSPACE_ID` | Solo si necesitas seleccionar el workspace; si no, no la crees. |

No pegues claves en el YAML, en issues o en capturas. Los colaboradores de un fork deben configurar sus propias claves en su repositorio; no recibirán las tuyas.

## 16. Crear `.github/workflows/eval-total.yml`

**Este es el fichero completo del pipeline.** Puedes crearlo en el editor local o en GitHub mediante **Add file → Create new file**, indicando esa ruta. Guardar un YAML dentro del Markdown no activa nada: hay que crear el fichero real y subir también sus dependencias del paso 14.

```yaml
name: Eval Delivery Board - total

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
  workflow_dispatch:

permissions:
  contents: read

jobs:
  eval-total:
    name: eval-total
    if: >-
      github.repository == 'jcarrenogallego/delivery-board' &&
      github.actor == 'jcarrenogallego' &&
      (github.event_name != 'pull_request' ||
       github.event.pull_request.head.repo.full_name == github.repository)
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
          EVAL_DESTINO: langsmith
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

No se ejecutan las instrucciones PowerShell en Ubuntu: el workflow contiene su equivalente Bash completo. El runner descarga las dependencias y compila los servicios; no usa instalaciones ni servidores que estén en tu ordenador.

Las claves de OpenAI y LangSmith solo se entregan al paso que ejecuta el eval, no a la instalación de paquetes ni a la compilación. En un entorno más estricto, fija también las acciones e imágenes por SHA/digest y revisa las actualizaciones periódicamente.

## 17. Ejecutarlo y ver los dos resultados

1. Sube los ficheros a una rama propia y después a `main`, o crea el workflow desde GitHub cuando el resto esté en `main`.
2. En **Actions**, abre **Eval Delivery Board - total**.
3. Puedes esperar al push o usar **Run workflow**, seleccionando `main`.
4. Abre el job **eval-total** y sus pasos.
5. En LangSmith, abre el dataset y el nuevo experimento del mismo prefijo.

| Resultado | Qué significa |
| --- | --- |
| LangSmith muestra `total_correcto = 1` y GitHub está verde | El único caso ha pasado. |
| LangSmith muestra `0` y GitHub está rojo | El script ha rechazado el comportamiento del agente. |
| GitHub falla antes de ejecutar el eval | Es un fallo de preparación, compilación, conexión o configuración; puede no existir experimento. |

El script comprueba que hubo **exactamente un caso evaluado y cero fallos**. Los errores de red o del modelo tampoco deben aprobar en silencio. No hay un despliegue automático: esta guía cubre CI, no CD.

Una vez que el check exista y esté validado, puedes marcar **eval-total** como comprobación obligatoria en una regla de protección de `main`. Esa regla es lo que impediría fusionar una PR sin un check aprobado; un job rojo por sí solo no impone una política de merge. No actives la exigencia antes del primer funcionamiento correcto.

---

# Parte 4 · Repositorio público, escritura solo del propietario

El repositorio es personal, propiedad de **`jcarrenogallego`**. En la revisión de preparación solo aparecía ese usuario como administrador; no había otros colaboradores, equipos ni claves de despliegue. La visibilidad era pública y los forks estaban permitidos.

Para mantener esa situación:

1. En **Settings → Collaborators / Manage access**, no invites a nadie con escritura.
2. Mantén el repositorio público. El equipo puede leer, clonar y crear forks sin ser colaborador.
3. El equipo sube sus cambios a **su fork**, no a tu `main`.
4. Puede proponerte una Pull Request. Tú decides qué revisar y fusionar; un fork no concede escritura al original.
5. No distribuyas tokens, claves de despliegue con escritura ni tu sesión de GitHub.

El workflow anterior tiene `contents: read`: **no puede subir cambios al repositorio con su token de trabajo**. Además, solo ejecuta el job con tu usuario, en tu repositorio y sin usar código de PRs procedentes de forks. No utiliza `pull_request_target`.

Por tanto, las PRs del equipo desde forks **no consumirán tus claves mediante este workflow**; el job aparecerá omitido. Para evaluarlas con tus secretos, primero revisa TODO el cambio, incluidas dependencias y workflows, y pásalo a una rama controlada por ti dentro del repositorio. No ejecutes código no confiable con tus claves simplemente por aprobar su ejecución.

Si el equipo quiere ejecutar este ejemplo en sus propios forks, debe cambiar el filtro de repositorio/usuario y configurar sus propios secretos y dataset. No les añadas tus claves. Para evaluar PRs no confiables sin secretos, se prepararía otro workflow con herramientas/modelos simulados; no forma parte de este único eval.

---

## Cerrar y repetir el laboratorio local

Detén con `Ctrl+C` **solo las terminales de API y MCP que abriste para este ejercicio**. Desde la raíz:

```powershell
docker compose -f evals/compose.eval.yml down
```

Esto elimina únicamente los contenedores del proyecto `delivery-board-eval`. No uses `docker system prune` ni borres volúmenes del tablero habitual. Al arrancar de nuevo este contenedor temporal, aplica migraciones y seed otra vez.

## Resumen final

1. En local: tres fixtures + agente real + comprobación en consola.
2. En LangSmith: un dataset, la regla de evaluación y experimentos comparables.
3. En GitHub: el mismo script, entorno temporal y error si no cumple.
4. Repositorio público para compartir; escritura del original reservada a tu cuenta.

**No se ha implementado ni ejecutado el pipeline al publicar esta guía.** Los resultados mostrados son esperados, no resultados de una validación real. Cada ejecución del agente consumirá llamadas al proveedor; LangSmith puede tener sus propios límites o costes de uso.

## Referencias

- [Evaluaciones de LangSmith](https://docs.langchain.com/langsmith/evaluation-quickstart).
- [Evaluadores de código en el SDK](https://docs.langchain.com/langsmith/code-evaluator-sdk).
- [Evaluadores de código en la web](https://docs.langchain.com/langsmith/code-evaluator-ui).
- [Secretos en GitHub Actions](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets).
- [Permisos en repositorios personales](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/repository-access-and-collaboration/permission-levels-for-a-personal-account-repository).
- [Acciones oficiales de Node.js](https://github.com/actions/setup-node) y [.NET](https://github.com/actions/setup-dotnet).
