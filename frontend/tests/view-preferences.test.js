const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const storageKey = "deliveryboard.view-preferences.v1";
function page(storage) {
  const nodes = new Map();
  const node = () => ({
    value: "All", textContent: "", innerHTML: "", disabled: false, callbacks: {},
    addEventListener(type, callback) { this.callbacks[type] = callback; },
    querySelectorAll() { return []; }, querySelector() { return node(); }, focus() {}
  });
  const context = vm.createContext({
    localStorage: storage,
    document: {
      activeElement: null,
      querySelector(selector) {
        if (!nodes.has(selector)) {
          const element = node();
          if (selector === "#sort-order") element.value = "base";
          nodes.set(selector, element);
        }
        return nodes.get(selector);
      },
      addEventListener() {}, removeEventListener() {}
    },
    fetch: () => new Promise(() => {}), queueMicrotask
  });
  for (const filename of ["board-view.js", "app.js"]) {
    vm.runInContext(fs.readFileSync(path.join(__dirname, "../public", filename), "utf8"), context);
  }
  return { nodes, change(selector, value) {
    const element = nodes.get(selector); element.value = value; element.callbacks.change();
  } };
}
function preferences(nodes) {
  return ["#status-filter", "#priority-filter", "#sort-order"].map(selector => nodes.get(selector).value);
}
function memoryStorage(initial) {
  const data = new Map(initial == null ? [] : [[storageKey, initial]]);
  return { data, getItem(key) { return data.get(key) ?? null; }, setItem(key, value) { data.set(key, value); } };
}

test("actual control callbacks save only preferences and a new page restores them", () => {
  const storage = memoryStorage();
  const first = page(storage);
  assert.deepEqual(preferences(first.nodes), ["All", "All", "base"]);
  first.change("#status-filter", "Blocked");
  first.change("#priority-filter", "High");
  first.change("#sort-order", "priority");
  assert.deepEqual(JSON.parse(storage.data.get(storageKey)), { status: "Blocked", priority: "High", order: "priority" });
  assert.equal(storage.data.size, 1);
  assert.deepEqual(preferences(page(storage).nodes), ["Blocked", "High", "priority"]);
});

test("invalid stored values fall back individually while valid selections survive", () => {
  const storage = memoryStorage(JSON.stringify({ status: "Unknown", priority: "Low", order: "descending", drafts: ["secret"] }));
  assert.deepEqual(preferences(page(storage).nodes), ["All", "Low", "base"]);
});

test("corrupt JSON and unexpected shapes restore defaults", () => {
  for (const value of ["{", "null", "42", "[]", '"hello"']) {
    assert.deepEqual(preferences(page(memoryStorage(value)).nodes), ["All", "All", "base"]);
  }
});

test("unavailable storage does not prevent controls from working", () => {
  const storage = { getItem() { throw new Error("denied"); }, setItem() { throw new Error("denied"); } };
  const current = page(storage);
  assert.deepEqual(preferences(current.nodes), ["All", "All", "base"]);
  assert.doesNotThrow(() => current.change("#priority-filter", "Medium"));
  assert.deepEqual(preferences(current.nodes), ["All", "Medium", "base"]);
});

test("a denied localStorage property is tolerated on read and write", () => {
  const context = vm.createContext({});
  vm.runInContext('Object.defineProperty(globalThis, "localStorage", { get() { throw new Error("denied"); } });', context);
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../public/board-view.js"), "utf8"), context);
  assert.equal(vm.runInContext("BoardView.loadPreferences().order", context), "base");
  assert.doesNotThrow(() => vm.runInContext('BoardView.savePreferences({ priority: "High" })', context));
});
