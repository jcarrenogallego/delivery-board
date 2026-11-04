const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

function board() {
  const nodes = new Map();
  const node = () => ({ value: 'All', textContent: '', innerHTML: '', disabled: false,
    addEventListener() {}, querySelectorAll() { return []; }, querySelector() { return node(); }, focus() {} });
  const context = vm.createContext({ document: { activeElement: null,
    querySelector(selector) {
      if (!nodes.has(selector)) {
        const element = node();
        if (selector === '#sort-order') element.value = 'base';
        nodes.set(selector, element);
      }
      return nodes.get(selector);
    },
    addEventListener() {}, removeEventListener() {} }, fetch: () => new Promise(() => {}), queueMicrotask });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../public/board-view.js'), 'utf8'), context);
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../public/app.js'), 'utf8'), context);
  const items = [
    { id: 'a', title: 'API <segura>', owner: 'Ana', priority: 'Medium', status: 'Pending', createdAt: '2026-10-14', prerequisiteIds: [] },
    { id: 'b', title: 'UI', owner: 'Luis', priority: 'High', status: 'Pending', createdAt: '2026-10-14', prerequisiteIds: ['a'] }
  ];
  context.inputItems = items;
  vm.runInContext('dashboard = { total: 2, pending: 2, inProgress: 0, completed: 0, items: inputItems }; render();', context);
  return { context, nodes, render: () => vm.runInContext('render()', context) };
}

test('blocked counter is subset of pending; pending start disabled and prerequisite title escaped', () => {
  const { nodes } = board();
  assert.equal(nodes.get('#blocked').textContent, 1);
  assert.equal(nodes.get('#pending').textContent, 2);
  assert.match(nodes.get('#work-items').innerHTML, /data-action="start" data-work-item-id="b" disabled/);
  assert.match(nodes.get('#work-items').innerHTML, /API &lt;segura&gt;/);
  assert.match(nodes.get('#work-items').innerHTML, /data-dependencies-id="b"/);
});

test('blocked filter shows only blocked tasks and completion removes blocking immediately', () => {
  const { nodes, context, render } = board();
  nodes.get('#status-filter').value = 'Blocked';
  render();
  assert.doesNotMatch(nodes.get('#work-items').innerHTML, /<strong>API/);
  assert.match(nodes.get('#work-items').innerHTML, /<strong>UI/);
  vm.runInContext('updateLocalItem("a", { ...dashboard.items[0], status: "Completed" })', context);
  assert.equal(nodes.get('#blocked').textContent, 0);
  assert.equal(nodes.get('#pending').textContent, 1);
  assert.match(nodes.get('#work-items').innerHTML, /No hay tareas/);
  nodes.get('#status-filter').value = 'Pending'; render();
  assert.match(nodes.get('#work-items').innerHTML, /data-action="start" data-work-item-id="b" >Iniciar/);
});

test('dependencies cannot be edited in progress or completed; repeated renders keep original data', () => {
  const { context, nodes, render } = board();
  vm.runInContext('dashboard.items[0].status = "Completed"; dashboard.items[1].status = "InProgress";', context);
  render();
  assert.equal(nodes.get('#blocked').textContent, 0);
  assert.doesNotMatch(nodes.get('#work-items').innerHTML, /data-dependencies-id/);
  assert.doesNotMatch(nodes.get('#work-items').innerHTML, /data-action="start"/);
});
