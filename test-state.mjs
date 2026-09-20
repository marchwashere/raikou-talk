import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { ReaderState, validateStrategy } from './public/reader-state.mjs';

const root = new URL('./public/', import.meta.url);
const manifest = JSON.parse(await readFile(new URL('manifest.json', root)));
const reports = [];
for (const band of manifest.bands) {
  const bytes = await readFile(new URL(band.url, root));
  assert.equal(createHash('sha256').update(bytes).digest('hex'), band.sha256);
  const data = JSON.parse(bytes), state = new ReaderState(data), origins = new Set();
  let visited = 0, terminals = 0;
  function walk(id, depth) {
    visited++;
    assert.equal(state.node, id); assert.equal(state.history.length, depth);
    assert.deepEqual(state.view, data.nodes[id]);
    if (state.view.terminal) {
      terminals++;
      for (const n of state.view.startingAdvances) { assert(!origins.has(n)); origins.add(n); }
      assert.equal(state.choose(0), false);
    } else {
      assert.deepEqual(state.view.startingAdvances, []);
      for (let index = 0; index < data.nodes[id].choices.length; index++) {
        const revision = state.revision;
        assert(state.choose(index, revision));
        const next = state.node;
        assert.equal(state.choose(0, revision), false); assert.equal(state.node, next);
        walk(next, depth + 1); assert(state.back()); assert.equal(state.node, id);
      }
    }
  }
  walk(0, 0); assert.equal(visited, data.nodes.length); assert.equal(origins.size, 214);
  assert.equal(state.back(), false);
  state.choose(0); state.restart(); assert.equal(state.node, 0); assert.deepEqual(state.history, []);
  const invalid = structuredClone(data); invalid.nodes[0].choices[0].next = 0;
  assert.throws(() => validateStrategy(invalid));
  const missing = structuredClone(data); missing.nodes[0].choices.pop();
  assert.throws(() => validateStrategy(missing));
  reports.push({ band: band.id, screens: visited, terminalRoutes: terminals, originalStarts: origins.size });
}
await mkdir(new URL('./verification/', import.meta.url), { recursive: true });
const report = { passed: true, reports };
await writeFile(new URL('./verification/state-tests.json', import.meta.url), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report));
