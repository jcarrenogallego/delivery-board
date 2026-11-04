const assert = require('node:assert/strict');
const { randomUUID } = require('node:crypto');
const base = new URL(process.argv[2] ?? 'http://127.0.0.1:15236');
if (base.protocol !== 'http:' || !['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname)
    || base.username || base.password || base.pathname !== '/' || base.search || base.hash)
  throw new Error('Supply only a local HTTP loopback origin.');
const ids = [];
let checks = 0;
async function request(method, route, body, expected = 200, raw = false) {
  const response = await fetch(new URL('/api/work-items' + route, base), {
    method, redirect: 'error', signal: AbortSignal.timeout(10000),
    headers: { 'content-type': 'application/json' },
    body: body === undefined ? undefined : raw ? body : JSON.stringify(body)
  });
  const text = await response.text();
  assert.equal(response.status, expected, `${method} ${route}: ${text}`);
  checks++;
  return text ? JSON.parse(text) : null;
}
async function create(extra = {}) {
  const item = await request('POST', '', { title: 'verification-comment-' + randomUUID(), owner: 'Ana', ...extra }, 201);
  ids.push(item.id);
  return item;
}
const commentless = ({ comment, ...rest }) => rest;
const metrics = ({ items, ...rest }) => rest;
(async () => {
 try {
  const openapi = await (await fetch(new URL('/openapi/v1.json', base), { redirect: 'error' })).json();
  assert.ok(openapi.paths['/api/work-items/{id}/comment'].patch);
  const control = await create({ comment: 'Control' });
  for (const extra of [{}, { comment: null }, { comment: '' }]) assert.equal((await create(extra)).comment, null);
  const literal = '  Unicode 🧪\n<script>alert("literal")</script>  ';
  const item = await create({ comment: literal });
  assert.equal(item.comment, literal);
  // A completed prerequisite keeps the target startable while exercising dependency preservation.
  const prerequisite = await create();
  await request('PATCH', `/${prerequisite.id}/complete`);
  await request('PATCH', `/${item.id}/dependencies`, { prerequisiteIds: [prerequisite.id] });
  for (const state of ['Pending', 'InProgress', 'Completed']) {
   if (state === 'InProgress') await request('PATCH', `/${item.id}/start`);
   if (state === 'Completed') await request('PATCH', `/${item.id}/complete`);
   for (const body of [{ comment: literal }, { comment: 'Replacement' }, { comment: '' }, { comment: null }, {}, { comment: '  ' }]) {
    const before = await request('GET', '/dashboard');
    const saved = await request('PATCH', `/${item.id}/comment`, body);
    assert.equal(saved.comment, body.comment === '' ? null : body.comment ?? null);
    assert.equal(saved.status, state);
    const after = await request('GET', '/dashboard');
    assert.deepEqual(metrics(after), metrics(before));
    assert.deepEqual(after.items.find(x => x.id === item.id), saved);
    assert.deepEqual(commentless(saved), commentless(before.items.find(x => x.id === item.id)));
    assert.deepEqual(after.items.filter(x => x.id !== item.id), before.items.filter(x => x.id !== item.id));
   }
  }
  const before = await request('GET', '/dashboard');
  for (const invalid of [42, true, [], {}]) {
   await request('PATCH', `/${item.id}/comment`, { comment: invalid }, 400);
   await request('POST', '', { title: 'invalid', owner: 'Ana', comment: invalid }, 400);
  }
  await request('PATCH', `/${item.id}/comment`, '{"comment":', 400, true);
  assert.deepEqual(await request('GET', '/dashboard'), before);
  await request('PATCH', `/${item.id}/comment`, { comment: 'Allowed', title: 'Injected', owner: 'Injected', status: 'Pending' });
  const updated = (await request('GET', '/dashboard')).items.find(x => x.id === item.id);
  assert.deepEqual(commentless(updated), commentless(before.items.find(x => x.id === item.id)));
  await request('DELETE', `/${item.id}`, undefined, 204);
  ids.splice(ids.indexOf(item.id), 1);
  const beforeMissing = await request('GET', '/dashboard');
  await request('PATCH', `/${item.id}/comment`, { comment: 'No recreation' }, 404);
  await request('PATCH', `/${randomUUID()}/comment`, { comment: '' }, 404);
  assert.deepEqual(await request('GET', '/dashboard'), beforeMissing);
  assert.equal((await request('GET', '/dashboard')).items.find(x => x.id === control.id).comment, 'Control');
  console.log(`PASS ${checks} HTTP checks: OpenAPI, POST/GET/PATCH, all states, empty/null/omitted, literals, binding 400, deleted 404, isolated writes, metrics, dependencies`);
 } finally {
  // Delete only IDs returned to this invocation; targets precede their prerequisite.
  for (const id of ids) await request('DELETE', `/${id}`, undefined, 204);
 }
})().catch(error => { console.error(error); process.exitCode = 1; });
