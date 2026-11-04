const assert = require("node:assert/strict");
const { randomUUID } = require("node:crypto");
const base = process.env.API_URL ?? "http://127.0.0.1:15202";
const ids = [];
async function request(method, path, body, status) {
  const response = await fetch(base + "/api/work-items" + path, {
    method, headers: { "content-type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body)
  });
  assert.equal(response.status, status, `${method} ${path}: ${await response.clone().text()}`);
  const text = await response.text();
  return text ? JSON.parse(text) : null;
}
async function create(priority, omit = false) {
  const item = await request("POST", "", { title: "VERIFICACION HTTP prioridad", owner: "Ana", ...(omit ? {} : { priority }) }, 201);
  ids.push(item.id);
  const persisted = (await request("GET", "/dashboard", undefined, 200)).items.find(x => x.id === item.id);
  assert.equal(persisted.priority, item.priority);
  return persisted;
}
(async () => {
  try {
    assert.equal((await create(null)).priority, "Medium");
    assert.equal((await create(undefined, true)).priority, "Medium");
    for (const priority of ["Low", "Medium", "High"]) assert.equal((await create(priority)).priority, priority);
    const before = await request("GET", "/dashboard", undefined, 200);
    for (const priority of ["Urgent", "3", "0", "Low, High", "", 3])
      await request("POST", "", { title: "invalid", owner: "Ana", priority }, 400);
    await request("POST", "", { title: " ", owner: "Ana", priority: "Low" }, 422);
    assert.deepEqual(await request("GET", "/dashboard", undefined, 200), before);
    const item = await create("Low");
    for (const status of ["Pending", "InProgress", "Completed"]) {
      if (status === "InProgress") {
        const start = await request("PATCH", `/${item.id}/start`, undefined, 200);
        assert.equal(start.changed, true);
        assert.equal((await request("PATCH", `/${item.id}/start`, undefined, 200)).changed, false);
      }
      if (status === "Completed") await request("PATCH", `/${item.id}/complete`, undefined, 200);
      for (const priority of ["Low", "Medium", "High"]) {
        const result = await request("PATCH", `/${item.id}/priority`, { priority }, 200);
        assert.equal(result.priority, priority);
        assert.equal(result.status, status);
        assert.equal(result.owner, "Ana");
        assert.equal(result.title, item.title);
        assert.equal(result.createdAt, item.createdAt);
        assert.deepEqual((await request("GET", "/dashboard", undefined, 200)).items.find(x => x.id === item.id), result);
      }
    }
    const invalidBefore = await request("GET", "/dashboard", undefined, 200);
    for (const priority of [null, "Urgent", "3", "0", "", 3]) await request("PATCH", `/${item.id}/priority`, { priority }, 400);
    await request("PATCH", `/${item.id}/priority`, {}, 400);
    await request("PATCH", `/${randomUUID()}/priority`, { priority: "Low" }, 404);
    await request("PATCH", `/${item.id}/owner`, { owner: "Bea", expectedOwner: "Ana" }, 409);
    await request("PATCH", `/${item.id}/start`, undefined, 409);
    assert.deepEqual(await request("GET", "/dashboard", undefined, 200), invalidBefore);
    const ownerItem = await create("Medium");
    const firstView = ownerItem;
    const secondView = { ...ownerItem };
    await request("PATCH", `/${firstView.id}/priority`, { priority: "High" }, 200);
    await request("PATCH", `/${secondView.id}/priority`, { priority: secondView.priority }, 200);
    await request("PATCH", `/${ownerItem.id}/owner`, { owner: "Bea", expectedOwner: "Ana" }, 200);
    const saved = (await request("GET", "/dashboard", undefined, 200)).items.find(x => x.id === ownerItem.id);
    assert.equal(saved.priority, "Medium");
    assert.equal(saved.owner, "Bea");
    await request("DELETE", `/${ownerItem.id}`, undefined, 204);
    await request("DELETE", `/${ownerItem.id}`, undefined, 404);
    console.log("PASS HTTP missing/null defaults; catalog and invalid 400; missing 404; all states/levels persisted; unrelated 422/409 unchanged; last save wins; five prior actions");
  } finally {
    for (const id of ids) await fetch(base + `/api/work-items/${id}`, { method: "DELETE" });
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
