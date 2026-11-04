const test = require("node:test");
const assert = require("node:assert/strict");
const { selectVisibleItems } = require("../public/board-view.js");

const items = [
  { id: "low-new", status: "Pending", priority: "Low", isBlocked: false },
  { id: "high-new", status: "Pending", priority: "High", isBlocked: true },
  { id: "medium", status: "InProgress", priority: "Medium", isBlocked: false },
  { id: "high-old", status: "Completed", priority: "High", isBlocked: false },
  { id: "low-old", status: "Pending", priority: "Low", isBlocked: true }
];
const ids = result => result.map(item => item.id);

test("defaults preserve the server base order and leave the input untouched", () => {
  const snapshot = structuredClone(items);
  const result = selectVisibleItems(items);
  assert.deepEqual(result, items);
  assert.notEqual(result, items);
  assert.deepEqual(items, snapshot);
});

test("priority ordering is High Medium Low and ties preserve base order", () => {
  assert.deepEqual(ids(selectVisibleItems(items, { order: "priority" })),
    ["high-new", "high-old", "medium", "low-new", "low-old"]);
  assert.equal(items[0].id, "low-new");
});

test("priority and status filters combine with AND without changing their options", () => {
  const options = { status: "Pending", priority: "High", order: "base" };
  assert.deepEqual(ids(selectVisibleItems(items, options)), ["high-new"]);
  assert.deepEqual(options, { status: "Pending", priority: "High", order: "base" });
  assert.deepEqual(ids(selectVisibleItems(items, { status: "Completed", priority: "High" })), ["high-old"]);
  assert.deepEqual(selectVisibleItems(items, { status: "InProgress", priority: "Low" }), []);
});

test("Blocked remains a subset of Pending and combines with priority", () => {
  assert.deepEqual(ids(selectVisibleItems(items, { status: "Blocked" })), ["high-new", "low-old"]);
  assert.deepEqual(ids(selectVisibleItems(items, { status: "Blocked", priority: "Low" })), ["low-old"]);
  assert.deepEqual(selectVisibleItems([{ id: "done", status: "Completed", priority: "High", isBlocked: true }],
    { status: "Blocked" }), []);
});

test("the chosen ordering applies to the filtered result only", () => {
  assert.deepEqual(ids(selectVisibleItems(items, { status: "Pending", order: "priority" })),
    ["high-new", "low-new", "low-old"]);
});

test("a confirmed priority edit re-evaluates the filter and ordering", () => {
  const updated = items.map(item => item.id === "low-new" ? { ...item, priority: "High" } : item);
  assert.deepEqual(ids(selectVisibleItems(updated, { priority: "Low" })), ["low-old"]);
  assert.deepEqual(ids(selectVisibleItems(updated, { priority: "High" })), ["low-new", "high-new", "high-old"]);
  assert.deepEqual(ids(selectVisibleItems(updated, { order: "priority" })),
    ["low-new", "high-new", "high-old", "medium", "low-old"]);
  assert.deepEqual(ids(selectVisibleItems(updated)), ids(items));
});

test("missing priority is treated as the Medium default", () => {
  const legacy = [{ id: "legacy", status: "Pending" }, { id: "high", status: "Pending", priority: "High" }];
  assert.deepEqual(ids(selectVisibleItems(legacy, { priority: "Medium" })), ["legacy"]);
  assert.deepEqual(ids(selectVisibleItems(legacy, { order: "priority" })), ["high", "legacy"]);
});
