const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const response = (body, status = 200) => ({ ok: status === 200, status, json: async () => body });
const success = (answer = "Dos tareas pendientes.", provider = "ollama") => response({ answer, provider, model: "local-model" });

function page() {
  const nodes = new Map();
  const calls = [];
  let handler = async () => success();
  const document = {
    querySelector(selector) {
      if (!nodes.has(selector)) nodes.set(selector, {
        value: selector === "#assistant-provider" ? "ollama" : "",
        textContent: "", disabled: false, hidden: selector === "#assistant-result", callbacks: {}, attributes: {},
        addEventListener(type, callback) { this.callbacks[type] = callback; },
        setAttribute(key, value) { this.attributes[key] = value; },
        focus() { this.focused = true; },
        set innerHTML(_) { throw new Error("Unsafe HTML rendering"); }
      });
      return nodes.get(selector);
    }
  };
  const context = vm.createContext({ document, fetch: async (url, options) => {
    calls.push({ url, options });
    return handler(url, options);
  } });
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../public/assistant.js"), "utf8"), context);
  const node = name => nodes.get(`#assistant-${name}`);
  const submit = () => node("form").callbacks.submit({ preventDefault() {} });
  return { node, calls, submit, fetchWith(fn) { handler = fn; } };
}

test("sends a trimmed independent question through relative API using selected provider", async () => {
  const p = page();
  p.node("question").value = "  ¿Qué hay pendiente?  ";
  await p.submit();
  assert.equal(p.calls[0].url, "/api/assistant/query");
  assert.equal(p.calls[0].options.method, "POST");
  assert.deepEqual(JSON.parse(p.calls[0].options.body), { provider: "ollama", question: "¿Qué hay pendiente?" });
  p.node("provider").value = "openai";
  p.fetchWith(() => success("Otra respuesta", "openai"));
  await p.submit();
  assert.deepEqual(JSON.parse(p.calls[1].options.body), { provider: "openai", question: "¿Qué hay pendiente?" });
  assert.equal(p.node("answer").textContent, "Otra respuesta");
});

test("rejects whitespace without an HTTP call and focuses the question", async () => {
  const p = page();
  p.node("question").value = " \n ";
  await p.submit();
  assert.equal(p.calls.length, 0);
  assert.match(p.node("status").textContent, /Escribe una pregunta/);
  assert.equal(p.node("question").focused, true);
});

test("disables controls, announces loading, clears old result and prevents duplicate submit", async () => {
  const p = page();
  p.node("question").value = "Pregunta";
  await p.submit();
  let resolve;
  p.fetchWith(() => new Promise(done => { resolve = done; }));
  const pending = p.submit();
  assert.equal(p.node("submit").disabled, true);
  assert.equal(p.node("provider").disabled, true);
  assert.equal(p.node("submit").textContent, "Consultando…");
  assert.equal(p.node("form").attributes["aria-busy"], "true");
  assert.equal(p.node("result").hidden, true);
  assert.equal(p.node("answer").textContent, "");
  await p.submit();
  assert.equal(p.calls.length, 2);
  resolve(success());
  await pending;
  assert.equal(p.node("submit").disabled, false);
  assert.equal(p.node("provider").disabled, false);
  assert.equal(p.node("form").attributes["aria-busy"], "false");
  assert.equal(p.node("submit").textContent, "Consultar");
});

test("renders literal generated text safely with Unicode and line breaks", async () => {
  const p = page();
  const text = '<script>alert("x")</script>\nSegunda línea 😃';
  p.node("question").value = "Pregunta";
  p.fetchWith(() => success(text));
  await p.submit();
  assert.equal(p.node("answer").textContent, text);
  assert.equal(p.node("result").hidden, false);
  assert.equal(p.node("status").textContent, "Respuesta recibida.");
});

for (const [code, message] of [[400, /Revisa/], [503, /no está disponible/], [504, /demasiado/], [502, /No se pudo/]]) {
  test(`HTTP ${code} shows safe error, preserves question and allows recovery`, async () => {
    const p = page();
    p.node("question").value = "Pregunta conservada";
    p.fetchWith(() => response({ detail: "SECRET" }, code));
    await p.submit();
    assert.match(p.node("status").textContent, message);
    assert.doesNotMatch(p.node("status").textContent, /SECRET/);
    assert.equal(p.node("question").value, "Pregunta conservada");
    assert.equal(p.node("result").hidden, true);
    assert.equal(p.node("submit").disabled, false);
    p.fetchWith(() => success());
    await p.submit();
    assert.equal(p.node("status").className, "assistant-status");
    assert.equal(p.node("result").hidden, false);
  });
}

for (const [name, handler] of [
  ["network failure", () => { throw new Error("SECRET transport details"); }],
  ["bad JSON", () => ({ ok: true, json: async () => { throw new SyntaxError("SECRET"); } })],
  ["empty answer", () => success(" ")],
  ["incorrect answer type", () => response({ answer: 5, provider: "ollama", model: "local" })],
  ["wrong provider", () => success("Respuesta", "openai")],
  ["missing model", () => response({ answer: "Respuesta", provider: "ollama" })]
]) {
  test(`${name} never displays partial or internal content`, async () => {
    const p = page();
    p.node("question").value = "Pregunta";
    p.fetchWith(handler);
    await p.submit();
    assert.match(p.node("status").textContent, /No se pudo obtener/);
    assert.equal(p.node("result").hidden, true);
    assert.equal(p.node("answer").textContent, "");
    assert.equal(p.node("provider").disabled, false);
  });
}

test("markup places independent accessible query panel between metrics and board", () => {
  const html = fs.readFileSync(path.join(__dirname, "../public/index.html"), "utf8");
  const css = fs.readFileSync(path.join(__dirname, "../public/styles.css"), "utf8");
  assert.ok(html.indexOf('class="metrics"') < html.indexOf('id="assistant-form"'));
  assert.ok(html.indexOf('id="assistant-form"') < html.indexOf('class="layout"'));
  assert.match(html, /role="status" aria-live="polite" aria-atomic="true"/);
  assert.match(html, /value="ollama" selected>Ollama \(local\)/);
  assert.match(html, /value="openai">OpenAI/);
  assert.match(html, /<script src="assistant.js"><\/script>/);
  assert.match(css, /#assistant-answer[^}]+white-space: pre-wrap/);
});
