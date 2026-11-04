const test = require("node:test");
const assert = require("node:assert/strict");
const http = require("node:http");
const { once } = require("node:events");
const { createServer } = require("../server.js");

async function listen(server, context) {
  server.listen(0, "127.0.0.1");
  await once(server, "listening");
  context.after(() => new Promise(resolve => {
    server.close(resolve);
    server.closeAllConnections();
  }));
  return `http://127.0.0.1:${server.address().port}`;
}

test("proxy forwards the question to the API and returns its complete JSON response", async context => {
  let received;
  const apiUrl = await listen(http.createServer(async (request, response) => {
    const chunks = [];
    for await (const chunk of request) chunks.push(chunk);
    received = { path: request.url, method: request.method, body: JSON.parse(Buffer.concat(chunks)) };
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify({ answer: "Hay tres tareas.\nUna está bloqueada.", provider: "ollama", model: "fixture" }));
  }), context);
  const frontendUrl = await listen(createServer({ apiUrl }), context);
  const response = await fetch(`${frontendUrl}/api/assistant/query`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ provider: "ollama", question: "¿Qué queda pendiente?" })
  });
  assert.equal(response.status, 200);
  assert.equal((await response.json()).answer, "Hay tres tareas.\nUna está bloqueada.");
  assert.deepEqual(received, {
    path: "/api/assistant/query", method: "POST",
    body: { provider: "ollama", question: "¿Qué queda pendiente?" }
  });
});

test("proxy preserves backend ProblemDetails and error status", async context => {
  const apiUrl = await listen(http.createServer((request, response) => {
    request.resume();
    response.writeHead(503, { "content-type": "application/problem+json" });
    response.end(JSON.stringify({ title: "Asistente no disponible", status: 503 }));
  }), context);
  const frontendUrl = await listen(createServer({ apiUrl }), context);
  const response = await fetch(`${frontendUrl}/api/assistant/query`, {
    method: "POST", headers: { "content-type": "application/json" }, body: "{}"
  });
  assert.equal(response.status, 503);
  assert.match(response.headers.get("content-type"), /application\/problem\+json/);
  assert.equal((await response.json()).status, 503);
});

test("disconnecting the browser aborts the pending backend request after uploading its body", async context => {
  const reachedBackend = Promise.withResolvers();
  const backendAborted = Promise.withResolvers();
  const apiUrl = await listen(http.createServer(async (request, response) => {
    for await (const chunk of request) { /* Consume the complete question before the browser disconnects. */ }
    response.on("close", () => backendAborted.resolve());
    reachedBackend.resolve();
  }), context);
  const frontendUrl = await listen(createServer({ apiUrl }), context);
  const browser = http.request(`${frontendUrl}/api/assistant/query`, {
    method: "POST", headers: { "content-type": "application/json" }
  });
  browser.on("error", () => {});
  browser.end(JSON.stringify({ provider: "ollama", question: "Una pregunta" }));
  await reachedBackend.promise;
  browser.destroy();
  const timeout = setTimeout(() => backendAborted.reject(new Error("Backend request was not cancelled")), 3000);
  try { await backendAborted.promise; }
  finally { clearTimeout(timeout); }
});

test("a completed proxy request does not affect a later independent request", async context => {
  let calls = 0;
  const apiUrl = await listen(http.createServer((request, response) => {
    request.resume();
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify({ answer: `Respuesta ${++calls}` }));
  }), context);
  const frontendUrl = await listen(createServer({ apiUrl }), context);
  for (const expected of [1, 2]) {
    const response = await fetch(`${frontendUrl}/api/assistant/query`, {
      method: "POST", headers: { "content-type": "application/json" }, body: "{}"
    });
    assert.equal((await response.json()).answer, `Respuesta ${expected}`);
  }
});
