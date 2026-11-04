// Solo servicios locales del proyecto; no requiere paquetes.
const base = process.argv[2] ?? "http://localhost:3000/api/work-items";
if (!["localhost", "127.0.0.1"].includes(new URL(base).hostname)) throw new Error("Usa la API local del proyecto.");
const ids = [];
function assert(condition, message) { if (!condition) throw new Error(message); }
async function request(path, method = "GET", body) {
  const response = await fetch(base + path, {
    method,
    headers: body ? { "content-type": "application/json" } : {},
    body: body ? JSON.stringify(body) : undefined
  });
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : null };
}
async function create(title) {
  const result = await request("", "POST", { title: `VERIFICACION HTTP ${title}`, owner: "Ana", priority: "High" });
  assert(result.status === 201, "Crear debe responder 201");
  ids.push(result.body.id);
  assert(result.body.status === "Pending", "Crear debe producir Pending");
  return result.body;
}
async function main() {
  try {
    const item = await create("inicio");
    const first = await request(`/${item.id}/start`, "PATCH");
    assert(first.status === 200 && first.body.changed === true, "Inicio debe cambiar");
    assert(first.body.workItem.id === item.id && first.body.workItem.status === "InProgress", "Snapshot de inicio");
    for (const key of ["title", "owner", "priority"]) assert(first.body.workItem[key] === item[key], `Campo intacto ${key}`);
    const dashboard = (await request("/dashboard")).body;
    assert(dashboard.items.find(x => x.id === item.id)?.status === "InProgress", "GET debe conservar inicio");
    const repeat = await request(`/${item.id}/start`, "PATCH");
    assert(repeat.status === 200 && repeat.body.changed === false, "Repetición sin cambio");
    assert(JSON.stringify(repeat.body.workItem) === JSON.stringify(first.body.workItem), "Snapshot repetición intacto");
    assert(JSON.stringify((await request("/dashboard")).body) === JSON.stringify(dashboard), "Repetición no cambia dashboard");
    const completed = await request(`/${item.id}/complete`, "PATCH");
    const completedAgain = await request(`/${item.id}/complete`, "PATCH");
    assert(completed.status === 200 && completed.body.status === "Completed", "Completar InProgress");
    assert(JSON.stringify(completed) === JSON.stringify(completedAgain), "Completar repetido intacto");
    const beforeErrors = (await request("/dashboard")).body;
    const conflict = await request(`/${item.id}/start`, "PATCH");
    assert(conflict.status === 409 && conflict.body.status === 409
      && conflict.body.title === "No se pudo completar la operación", "Completed debe devolver ProblemDetails 409");
    const missing = await request("/00000000-0000-0000-0000-000000000001/start", "PATCH");
    assert(missing.status === 404 && missing.body === null, "Ausente devuelve 404 vacío");
    assert(JSON.stringify((await request("/dashboard")).body) === JSON.stringify(beforeErrors), "Errores no cambian dashboard");
    const pending = await create("completar pendiente");
    const direct = await request(`/${pending.id}/complete`, "PATCH");
    assert(direct.status === 200 && direct.body.status === "Completed", "Completar Pending directamente");
    console.log("CA-2/4/5/6/7/8/9 HTTP PASS (200 changed, persisted GET, no-op, 409 ProblemDetails, 404, complete both states)");
  } finally {
    for (const id of ids) await request(`/${id}`, "DELETE");
  }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
