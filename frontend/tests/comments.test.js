const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const item = (id = "a", status = "Pending") => ({ id, status, title: "Tarea", owner: "Ana", priority: "Medium", createdAt: "2026-10-16", prerequisiteIds: [], comment: null });
const response = (body, status = 200) => ({ status, ok: status >= 200 && status < 300, json: async () => body });
function page(items = [item()]) {
  const nodes = new Map();
  const controls = [];
  const listeners = {};
  const document = { activeElement: null, body: {},
    addEventListener(type, callback) { listeners[type] = callback; },
    removeEventListener(type) { delete listeners[type]; },
    querySelector(selector) {
      if (!nodes.has(selector)) nodes.set(selector, node(selector === "#sort-order" ? "base" : "All"));
      return nodes.get(selector);
    }
  };
  function node(value) { return { value, dataset: {}, callbacks: {}, disabled: false, textContent: "", innerHTML: "",
    addEventListener(type, callback) { this.callbacks[type] = callback; },
    querySelectorAll() { return controls; }, querySelector() { return node(); },
    closest() { return { setAttribute() {} }; },
    focus() { document.activeElement = this; },
    setSelectionRange(start, end, direction) { this.selectionStart = start; this.selectionEnd = end; this.selectionDirection = direction; }
  }; }
  const context = vm.createContext({ document, fetch: () => new Promise(() => {}), queueMicrotask,
    FormData: class { constructor(form) { return Object.entries(form.fields); } }
  });
  for (const filename of ["board-view.js", "app.js"]) vm.runInContext(fs.readFileSync(path.join(__dirname, "../public", filename), "utf8"), context);
  context.fixture = { items, total: items.length, pending: items.filter(i => i.status === "Pending").length, inProgress: items.filter(i => i.status === "InProgress").length, completed: items.filter(i => i.status === "Completed").length };
  vm.runInContext("dashboard = fixture; dashboardRequests = 0; render();", context);
  const evaluate = code => vm.runInContext(code, context);
  const calls = [];
  function fetchWith(handler) { context.fetch = async (url, options) => { calls.push({ url, options }); return handler(url, options); }; }
  const input = (id, value) => nodes.get("#work-items").callbacks.input({ target: { dataset: { commentInputId: id }, value } });
  return { context, nodes, controls, listeners, document, node, evaluate, calls, fetchWith, input };
}

test("editors exist in every state, escape content, keep drafts across filters and completion", () => {
  const p = page([item(), item("b", "InProgress"), item("c", "Completed")]);
  const text = '\n </textarea><script>alert("x")</script> & 😃\n';
  p.input("a", text);
  p.evaluate('ownerDrafts.set("a", { owner: "Borrador", expectedOwner: "Ana", error: "" }); render();');
  const html = p.nodes.get("#work-items").innerHTML;
  assert.equal((html.match(/data-comment-form-id=/g) ?? []).length, 3);
  assert.match(html, /&lt;\/textarea&gt;&lt;script&gt;/);
  assert.doesNotMatch(html, /<script>/);
  p.nodes.get("#status-filter").value = "Completed";
  p.evaluate('render(); dashboard.items[0].status = "Completed"; render();');
  assert.equal(p.evaluate('commentDrafts.get("a")'), text);
  p.evaluate('dashboard.items = dashboard.items.filter(i => i.id !== "a"); render();');
  assert.equal(p.evaluate('commentDrafts.has("a")'), false);
});

test("200 changes only comment, preserves other drafts and counters when refresh fails", async () => {
  const p = page([item(), item("b")]);
  p.input("a", "  Texto\nUnicode 😃  ");
  p.input("b", "Otro borrador");
  p.evaluate('ownerDrafts.set("a", { owner: "Bea", expectedOwner: "Ana", error: "" });');
  p.fetchWith((url) => {
    if (url.endsWith("/dashboard")) throw new Error("offline");
    return response({ ...item(), comment: "  Texto\nUnicode 😃  ", status: "Completed", owner: "Otro", priority: "High" });
  });
  await p.evaluate('saveComment("a")');
  assert.deepEqual(JSON.parse(p.calls[0].options.body), { comment: "  Texto\nUnicode 😃  " });
  assert.equal(p.calls[0].options.method, "PATCH");
  assert.equal(p.evaluate('dashboard.items[0].owner'), "Ana");
  assert.equal(p.evaluate('dashboard.items[0].status'), "Pending");
  assert.equal(p.evaluate('dashboard.pending'), 2);
  assert.equal(p.evaluate('commentDrafts.has("a")'), false);
  assert.equal(p.evaluate('commentDrafts.get("b")'), "Otro borrador");
  assert.equal(p.evaluate('ownerDrafts.get("a").owner'), "Bea");
  assert.match(p.nodes.get("#message").textContent, /Comentario guardado correctamente.*No se pudo actualizar/);
});

for (const status of ["Pending", "InProgress", "Completed"]) {
  for (const previous of [null, "Anterior"]) test(`add or replace ${previous === null ? "absent" : "existing"} comment in ${status}`, async () => {
    const p = page([{ ...item("a", status), comment: previous }]);
    p.input("a", "Nuevo texto");
    p.fetchWith(url => url.endsWith("/dashboard") ? response({ ...p.context.fixture, items: [{ ...item("a", status), comment: "Nuevo texto" }] }) : response({ ...item("a", status), comment: "Nuevo texto" }));
    await p.evaluate('saveComment("a")');
    assert.equal(p.evaluate('dashboard.items[0].comment'), "Nuevo texto");
    assert.equal(p.evaluate('dashboard.items[0].status'), status);
    assert.match(p.nodes.get("#message").textContent, /Comentario guardado correctamente/);
  });
  test(`empty explicitly deletes in ${status}`, async () => {
    const p = page([{ ...item("a", status), comment: "Anterior" }]);
    p.input("a", "");
    p.fetchWith(url => url.endsWith("/dashboard") ? response({ ...p.context.fixture, items: [{ ...item("a", status), comment: null }] }) : response({ ...item("a", status), comment: null }));
    await p.evaluate('saveComment("a")');
    assert.deepEqual(JSON.parse(p.calls[0].options.body), { comment: "" });
    assert.equal(p.evaluate('dashboard.items[0].comment'), null);
  });
}

test("legacy missing comment and null both render an empty optional editor", () => {
  const legacy = item(); delete legacy.comment;
  const p = page([legacy, item("b")]);
  assert.equal((p.nodes.get("#work-items").innerHTML.match(/>\n<\/textarea>/g) ?? []).length, 2);
  assert.doesNotMatch(p.nodes.get("#work-items").innerHTML, /textarea[^>]+(?:required|maxlength)/);
});

for (const [name, result] of [
  ["HTTP", () => response({}, 500)], ["network", () => { throw new Error("offline"); }],
  ["truncated JSON", () => ({ status: 200, json: async () => { throw new SyntaxError(); } })],
  ["missing comment", () => response({ id: "a" })], ["wrong type", () => response({ id: "a", comment: 42 })],
  ["wrong id", () => response({ id: "b", comment: "Text" })], ["unexpected status", () => response({ id: "a", comment: "Text" }, 201)]
]) test(`uncertain ${name} retains draft and never retries or confirms`, async () => {
  const p = page(); p.input("a", "Borrador"); p.fetchWith(result);
  await p.evaluate('saveComment("a")');
  assert.equal(p.calls.length, 1);
  assert.equal(p.evaluate('commentDrafts.get("a")'), "Borrador");
  assert.equal(p.evaluate('dashboard.items[0].comment'), null);
  assert.match(p.nodes.get("#message").textContent, /No se pudo confirmar.*Actualizar/);
  assert.equal(p.nodes.get("#refresh").disabled, false);
});

test("404 removes vanished task and reconciles without creating a replacement", async () => {
  const p = page(); p.input("a", "Texto");
  p.fetchWith(url => url.endsWith("/dashboard") ? response({ total: 0, pending: 0, inProgress: 0, completed: 0, items: [] }) : response({}, 404));
  await p.evaluate('saveComment("a")');
  assert.equal(p.evaluate("dashboard.items.length"), 0);
  assert.equal(p.evaluate('commentDrafts.has("a")'), false);
  assert.match(p.nodes.get("#message").textContent, /La tarea ya no existe/);
  assert.ok(p.calls.every(call => call.options?.method !== "POST"));
});

test("blocks same card and refresh, rejects duplicate send, ignores stale reads", async () => {
  const p = page([item(), item("b")]);
  p.controls.push(Object.assign(p.node(), { dataset: { commentInputId: "a" } }), Object.assign(p.node(), { dataset: { workItemId: "b", action: "delete" } }));
  let resolveRead; let resolveSave;
  p.fetchWith(url => new Promise(resolve => { if (url.endsWith("/dashboard")) resolveRead = resolve; else resolveSave = resolve; }));
  const oldRead = p.evaluate("loadDashboard()");
  const save = p.evaluate('saveComment("a")');
  await p.evaluate('saveComment("a")');
  assert.equal(p.calls.length, 2);
  assert.equal(p.controls[0].disabled, true);
  assert.equal(p.controls[1].disabled, false);
  assert.equal(p.nodes.get("#refresh").disabled, true);
  resolveRead(response({ ...p.context.fixture, items: [] })); await oldRead;
  assert.equal(p.evaluate("dashboard.items.length"), 2);
  resolveSave(response({}, 500)); await save;
  assert.equal(p.controls[0].disabled, false);
});

test("render preserves textarea focus and selection; save does not steal moved focus", async () => {
  const p = page();
  const control = Object.assign(p.node(), { dataset: { commentInputId: "a" }, selectionStart: 1, selectionEnd: 3, selectionDirection: "backward" });
  p.controls.push(control); p.document.activeElement = control;
  p.evaluate("render()");
  assert.equal(p.document.activeElement, control);
  assert.equal(control.selectionEnd, 3);
  let resolve;
  p.fetchWith(() => new Promise(done => { resolve = done; }));
  const save = p.evaluate('saveComment("a")');
  const elsewhere = Object.assign(p.node(), { dataset: {} });
  p.document.activeElement = elsewhere; p.listeners.focusin({ target: elsewhere });
  resolve(response({}, 500)); await save;
  assert.equal(p.document.activeElement, elsewhere);
  assert.equal(p.listeners.focusin, undefined);
});

test("creation sends optional comment verbatim, retains on failure and resets only on success", async () => {
  const p = page(); const form = p.nodes.get("#work-item-form");
  form.fields = { title: "Nueva", owner: "Ana", priority: "Medium", comment: "  Texto\n😃  " };
  let resets = 0; form.reset = () => { resets++; };
  p.fetchWith(() => response({}, 500));
  await form.callbacks.submit({ preventDefault() {} });
  assert.equal(resets, 0);
  assert.equal(JSON.parse(p.calls[0].options.body).comment, "  Texto\n😃  ");
  form.fields.comment = "";
  p.fetchWith(url => url.endsWith("/dashboard") ? response(p.context.fixture) : response({}, 201));
  await form.callbacks.submit({ preventDefault() {} });
  assert.equal(resets, 1);
  assert.equal(JSON.parse(p.calls[1].options.body).comment, "");
  const html = fs.readFileSync(path.join(__dirname, "../public/index.html"), "utf8");
  assert.match(html, /<textarea id="new-comment" name="comment" rows="4"><\/textarea>/);
});
