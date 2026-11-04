const http = require("node:http");
const fs = require("node:fs/promises");
const path = require("node:path");

const port = Number(process.env.PORT ?? 3000);
const publicDirectory = path.join(__dirname, "public");

function getApiUrl() {
  return process.env.API_URL
    ?? process.env.services__api__http__0
    ?? process.env.services__api__https__0
    ?? "http://localhost:5080";
}

const contentTypes = {
  ".css": "text/css; charset=utf-8",
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8"
};

async function proxyToApi(request, response, apiUrl) {
  const cancellation = new AbortController();
  const abortRequest = () => cancellation.abort();
  const abortIfDisconnected = () => {
    if (!response.writableFinished) cancellation.abort();
  };
  request.on("aborted", abortRequest);
  response.on("close", abortIfDisconnected);
  try {
    const hasBody = request.method !== "GET" && request.method !== "HEAD";
    const apiResponse = await fetch(new URL(request.url, apiUrl ?? getApiUrl()), {
      method: request.method,
      headers: { "content-type": request.headers["content-type"] ?? "application/json" },
      body: hasBody ? request : undefined,
      duplex: hasBody ? "half" : undefined,
      signal: cancellation.signal
    });

    const body = Buffer.from(await apiResponse.arrayBuffer());
    if (response.destroyed) return;
    response.writeHead(apiResponse.status, {
      "content-type": apiResponse.headers.get("content-type") ?? "application/json"
    });
    response.end(body);
  } catch (error) {
    if (!cancellation.signal.aborted) throw error;
  } finally {
    request.off("aborted", abortRequest);
    response.off("close", abortIfDisconnected);
  }
}

async function serveStatic(request, response) {
  const requestedPath = request.url === "/" ? "/index.html" : request.url;
  const filePath = path.join(publicDirectory, path.normalize(requestedPath));

  if (!filePath.startsWith(publicDirectory)) {
    response.writeHead(403).end("Forbidden");
    return;
  }

  try {
    const content = await fs.readFile(filePath);
    response.writeHead(200, { "content-type": contentTypes[path.extname(filePath)] ?? "application/octet-stream" });
    response.end(content);
  } catch {
    response.writeHead(404).end("Not found");
  }
}

function createServer({ apiUrl } = {}) {
  return http.createServer(async (request, response) => {
    try {
      if (request.url.startsWith("/api/")) {
        await proxyToApi(request, response, apiUrl);
        return;
      }

      await serveStatic(request, response);
    } catch (error) {
      if (response.destroyed || response.writableEnded) return;
      console.error(error);
      response.writeHead(502, { "content-type": "application/json" });
      response.end(JSON.stringify({ detail: "No fue posible comunicarse con la API." }));
    }
  });
}

if (require.main === module) {
  createServer().listen(port, () => console.log(`Delivery Board disponible en http://localhost:${port}`));
}

module.exports = { createServer };
