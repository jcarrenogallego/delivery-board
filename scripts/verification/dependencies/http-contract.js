// Run only against a local API: node scripts/verification/dependencies/http-contract.js http://127.0.0.1:15202
// Creates uniquely named tasks and cleans up only those IDs, including on a failed assertion.
const assert = require('node:assert/strict');
const { randomUUID } = require('node:crypto');

const base = new URL(process.argv[2] ?? 'http://127.0.0.1:15202');
if (base.protocol !== 'http:' || !['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname)
  || base.username || base.password || base.pathname !== '/' || base.search || base.hash) {
  throw new Error('Verification is local-only: supply an HTTP loopback origin, without credentials/path/query.');
}

const created = new Set();
const prefix = `verification-dependencies-${randomUUID()}`;
let checks = 0;
async function request(method, route, body, expected = 200) {
  const response = await fetch(new URL(`/api/work-items${route}`, base), {
    method, redirect: 'error', signal: AbortSignal.timeout(10000),
    headers: body === undefined ? {} : { 'content-type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const text = await response.text();
  const payload = text ? JSON.parse(text) : null;
  assert.equal(response.status, expected, `${method} ${route}: ${text}`);
  checks++;
  return payload;
}
async function create(label) {
  const item = await request('POST', '/', { title: `${prefix}-${label}`, owner: 'Verification', priority: 'Medium' }, 201);
  created.add(item.id);
  return item;
}
async function dashboard() {
  const result = await request('GET', '/dashboard');
  assert.equal(result.pending, result.items.filter(item => item.status === 'Pending').length);
  assert.equal(result.blocked, result.items.filter(item => item.isBlocked).length);
  assert.ok(result.items.filter(item => item.isBlocked).every(item => item.status === 'Pending'));
  assert.equal(result.total, result.pending + result.inProgress + result.completed);
  return result;
}
const dependencies = (id, ids, status = 200) => request('PATCH', `/${id}/dependencies`, { prerequisiteIds: ids }, status);
const find = (result, id) => result.items.find(item => item.id === id);

async function cleanup() {
  if (!created.size) return;
  // Clear dependencies while editable; started/completed tasks are removed before their prerequisites.
  let current = await request('GET', '/dashboard');
  for (const item of current.items.filter(item => created.has(item.id) && item.status === 'Pending')) {
    await dependencies(item.id, []);
  }
  while (created.size) {
    current = await request('GET', '/dashboard');
    const remaining = current.items.filter(item => created.has(item.id));
    for (const id of [...created]) if (!remaining.some(item => item.id === id)) created.delete(id);
    if (!created.size) break;
    const deletable = remaining.filter(item => !current.items.some(other => (other.prerequisiteIds ?? []).includes(item.id)));
    assert.ok(deletable.length, `Cleanup blocked; IDs left: ${[...created].join(', ')}`);
    for (const item of deletable) {
      await request('DELETE', `/${item.id}`, undefined, 204);
      created.delete(item.id);
    }
  }
}

async function main() {
  try {
    const target = await create('target');
    const first = await create('first');
    const second = await create('second');
    const missing = randomUUID();
    await dependencies(target.id, [target.id], 400);
    await dependencies(target.id, [first.id, first.id], 400);
    await dependencies(target.id, [missing], 404);
    await dependencies(missing, [], 404);
    await request('PATCH', `/${target.id}/dependencies`, { prerequisiteIds: null }, 400);

    await dependencies(first.id, [second.id]);
    await dependencies(target.id, [first.id, second.id]);
    await dependencies(second.id, [target.id], 400);
    const persisted = await dashboard();
    assert.deepEqual(find(persisted, target.id).prerequisiteIds, [first.id, second.id]);
    assert.equal(find(persisted, target.id).isBlocked, true);
    assert.equal(find(persisted, first.id).isBlocked, true);
    assert.equal(find(persisted, second.id).isBlocked, false);
    const blocked = await request('PATCH', `/${target.id}/start`, undefined, 409);
    assert.ok(blocked.detail.includes(first.title) && blocked.detail.includes(second.title));
    await request('DELETE', `/${second.id}`, undefined, 409);

    await request('PATCH', `/${target.id}/priority`, { priority: 'High' });
    await request('PATCH', `/${target.id}/owner`, { owner: 'Verification updated', expectedOwner: 'Verification' });
    const coexist = find(await dashboard(), target.id);
    assert.equal(coexist.priority, 'High');
    assert.equal(coexist.owner, 'Verification updated');
    assert.deepEqual(coexist.prerequisiteIds, [first.id, second.id]);
    assert.equal(coexist.isBlocked, true);

    await request('PATCH', `/${second.id}/complete`);
    const partiallyDone = await dashboard();
    assert.equal(find(partiallyDone, first.id).isBlocked, false);
    assert.equal(find(partiallyDone, target.id).isBlocked, true);
    await request('PATCH', `/${target.id}/start`, undefined, 409);
    await dependencies(second.id, [], 409);
    await request('PATCH', `/${first.id}/complete`);
    assert.equal(find(await dashboard(), target.id).isBlocked, false);
    const started = await request('PATCH', `/${target.id}/start`);
    assert.equal(started.changed, true);
    assert.equal(started.workItem.status, 'InProgress');
    const repeated = await request('PATCH', `/${target.id}/start`);
    assert.equal(repeated.changed, false);
    await dependencies(target.id, [], 409);
    await request('PATCH', `/${target.id}/complete`);
    await request('PATCH', `/${target.id}/start`, undefined, 409);
    await dependencies(target.id, [], 409);
    const final = find(await dashboard(), target.id);
    assert.equal(final.status, 'Completed');
    assert.equal(final.isBlocked, false);
    assert.deepEqual(final.prerequisiteIds, [first.id, second.id]);
    assert.equal(final.priority, 'High');
    assert.equal(final.owner, 'Verification updated');

    // Opposite edges submitted concurrently must not both persist (no cycle under concurrent writes).
    const left = await create('concurrent-left');
    const right = await create('concurrent-right');
    const races = await Promise.all([
      fetch(new URL(`/api/work-items/${left.id}/dependencies`, base), { method: 'PATCH', redirect: 'error', signal: AbortSignal.timeout(10000), headers: { 'content-type': 'application/json' }, body: JSON.stringify({ prerequisiteIds: [right.id] }) }),
      fetch(new URL(`/api/work-items/${right.id}/dependencies`, base), { method: 'PATCH', redirect: 'error', signal: AbortSignal.timeout(10000), headers: { 'content-type': 'application/json' }, body: JSON.stringify({ prerequisiteIds: [left.id] }) })
    ]);
    assert.deepEqual(races.map(result => result.status).sort(), [200, 400]);
    for (const response of races) await response.text();
    checks += 2;
    console.log(`PASS: dependencies HTTP contract (${checks} requests/assertions before cleanup), including concurrency.`);
  } finally {
    await cleanup();
    console.log('Cleanup complete: only verification-created tasks were deleted.');
  }
}
main().catch(error => { console.error(error); if (created.size) console.error('Cleanup pending IDs:', [...created]); process.exitCode = 1; });
