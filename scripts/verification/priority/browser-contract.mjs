import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";

// Run in cua_repl with an existing CUA tab on the isolated local frontend.
// All mutations concern fixtures created by this run; interceptions are cleared in finally.
async function checkPriorityBrowserContract(tab) {
  const cdp = await tab.capabilities.get("cdp");
  const fixtures = [];
  const passed = [];
  const form = '#work-item-form';
  const titleInput = tab.playwright.locator(`${form} input[name="title"]`);
  const ownerInput = tab.playwright.locator(`${form} input[name="owner"]`);
  const clear = () => cdp.send("Fetch.enable", { patterns: [] });
  let cursor = (await cdp.readEvents({ methods: ["Fetch.requestPaused"], timeoutMs: 1 })).cursor;
  let failure;
  const origin = await titleInput.evaluate(() => location.origin);
  assert.match(origin, /^http:\/\/(?:localhost|127\.0\.0\.1):13000$/, "Use the isolated priority frontend");

  async function paused(method, suffix) {
    const result = await cdp.readEvents({ afterSequence: cursor, methods: ["Fetch.requestPaused"], timeoutMs: 5000 });
    cursor = result.cursor;
    const matching = result.events.filter(event => event.params.request.method === method && event.params.request.url.endsWith(suffix));
    assert.equal(matching.length, 1, `Expected one paused ${method} ${suffix}`);
    return matching[0].params.requestId;
  }
  function fulfill(requestId, data, responseCode = 200) {
    return cdp.send("Fetch.fulfillRequest", {
      requestId, responseCode,
      responseHeaders: [{ name: "Content-Type", value: "application/json" }],
      body: Buffer.from(JSON.stringify(data)).toString("base64")
    });
  }
  function intercept(id, dashboard = false) {
    const urls = [`${origin}/api/work-items/${id}/priority`];
    if (dashboard) urls.push(`${origin}/api/work-items/dashboard`);
    return cdp.send("Fetch.enable", {
      patterns: urls.map(urlPattern => ({ urlPattern, resourceType: "Fetch", requestStage: "Request" }))
    });
  }
  function selector(title) {
    return tab.playwright.getByRole("combobox", { name: `Prioridad de ${title}`, exact: true });
  }
  async function unlocked(id) {
    await tab.playwright.locator(`select[data-work-item-id="${id}"]:not([disabled])`).waitFor({ state: "visible" });
  }
  async function idle() {
    await tab.playwright.locator('#refresh:not([disabled])').waitFor({ state: "visible" });
  }
  async function create(title, wait = true) {
    fixtures.push(title);
    await titleInput.fill(title);
    await ownerInput.fill("Browser priority verification");
    await tab.playwright.locator(`${form} select[name="priority"]`).selectOption("Medium");
    await tab.playwright.getByRole("button", { name: "Crear tarea", exact: true }).click();
    if (wait) {
      await selector(title).waitFor({ state: "visible" });
      await idle();
      return selector(title).getAttribute("data-work-item-id");
    }
  }
  async function state(id) {
    return tab.playwright.locator(`select[data-work-item-id="${id}"]`).evaluate(element => ({
      priority: element.value,
      disabled: element.disabled,
      busy: element.closest('.work-item').getAttribute('aria-busy'),
      controlsDisabled: [...element.closest('.work-item').querySelectorAll('button')].some(button => button.disabled),
      refreshDisabled: document.querySelector('#refresh').disabled,
      message: document.querySelector('#message').textContent,
      focusedTitle: document.activeElement === document.querySelector('#work-item-form input[name="title"]'),
      titleDraft: document.querySelector('#work-item-form input[name="title"]').value
    }));
  }
  try {
    await clear();
    await tab.reload();
    await idle();
    const title = `BROWSER priority ${randomUUID()}`;
    const id = await create(title);
    const priority = selector(title);
    for (const scenario of ["400", "network", "incomplete200", "array200"]) {
      await intercept(id);
      const action = priority.selectOption("High");
      const requestId = await paused("PATCH", `/${id}/priority`);
      assert.equal((await state(id)).disabled, true, `${scenario}: selector must lock`);
      if (scenario === "network") await cdp.send("Fetch.failRequest", { requestId, errorReason: "Failed" });
      else await fulfill(requestId, scenario === "400" ? { detail: "invalid" }
        : scenario === "array200" ? { id, priority: ["High"] } : {}, scenario === "400" ? 400 : 200);
      await action;
      await unlocked(id);
      const result = await state(id);
      assert.equal(result.priority, "Medium", `${scenario}: restore last confirmed priority`);
      assert.equal(result.busy, "false");
      assert.equal(result.controlsDisabled, false);
      assert.equal(result.refreshDisabled, false);
      assert.match(result.message, /Actualizar/);
      assert.doesNotMatch(result.message, /guardada correctamente/);
      passed.push(`${scenario}: rollback, recovery message, all controls released`);
      await clear();
    }

    await intercept(id, true);
    let action = priority.selectOption("High");
    await fulfill(await paused("PATCH", `/${id}/priority`), { id, priority: "High" });
    await action;
    await cdp.send("Fetch.failRequest", { requestId: await paused("GET", "/dashboard"), errorReason: "Failed" });
    await unlocked(id);
    let result = await state(id);
    assert.equal(result.priority, "High");
    assert.match(result.message, /Prioridad guardada correctamente/);
    assert.match(result.message, /No se pudo actualizar/);
    assert.equal(result.refreshDisabled, false);
    passed.push("confirmed PATCH + failed GET: confirmed priority retained");
    await clear();
    await priority.selectOption("Medium");
    await unlocked(id);
    await idle();

    await intercept(id);
    action = priority.selectOption("High");
    const focusRequest = await paused("PATCH", `/${id}/priority`);
    const draft = `Creation draft ${randomUUID()}`;
    await titleInput.fill(draft);
    await fulfill(focusRequest, {}, 400);
    await action;
    await unlocked(id);
    result = await state(id);
    assert.equal(result.focusedTitle, true, "Priority completion must not steal creation focus");
    assert.equal(result.titleDraft, draft, "Typed creation text must survive");
    assert.equal(result.priority, "Medium");
    passed.push("focus moved to creation: focused input and typed draft retained");
    await clear();

    await intercept(id, true);
    action = priority.selectOption("High");
    await fulfill(await paused("PATCH", `/${id}/priority`), { id, priority: "High" });
    await action;
    const refreshA = await paused("GET", "/dashboard");
    const secondTitle = `BROWSER creation ${randomUUID()}`;
    await create(secondTitle, false);
    const refreshB = await paused("GET", "/dashboard");
    // A is superseded; complete its operation before allowing B's newer read to arrive.
    await cdp.send("Fetch.continueRequest", { requestId: refreshA });
    await unlocked(id);
    assert.equal((await state(id)).refreshDisabled, true, "B must still be pending after A finishes");
    await cdp.send("Fetch.continueRequest", { requestId: refreshB });
    await idle();
    await selector(secondTitle).waitFor({ state: "visible" });
    assert.equal(await selector(secondTitle).evaluate(element => element.value), "Medium");
    passed.push("priority refresh A, creation refresh B, A finishes first: B still applies");
  } catch (error) {
    failure = error;
  } finally {
    // Clearing first also releases paused requests if an assertion failed mid-scenario.
    try {
      await clear();
      await tab.reload();
      await idle();
      for (const title of fixtures) {
        const fixture = selector(title);
        const id = await fixture.getAttribute("data-work-item-id");
        if (!id) continue;
        await tab.playwright.locator(`button[data-work-item-id="${id}"][data-action="delete"]`).click();
        await tab.playwright.locator('#confirm-dialog-accept').click();
        await fixture.waitFor({ state: "detached" });
        await idle();
      }
    } catch (cleanupError) {
      if (!failure) failure = cleanupError;
      else failure.message += `; fixture cleanup failed: ${cleanupError.message}`;
    } finally {
      await clear();
    }
  }
  if (failure) throw failure;
  return { passed };
}

export { checkPriorityBrowserContract };
