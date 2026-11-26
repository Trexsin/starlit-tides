import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cleanName, validateSubmission, parseLimit } from '../lib/validate.js';

const good = { name: 'Mira', score: 4210, orbs: 37, distance: 1650, duration: 142, version: '1.0' };

test('accepts a normal run', () => {
  const r = validateSubmission(good);
  assert.equal(r.ok, true);
  assert.deepEqual(r.value, good);
});

test('accepts numeric strings', () => {
  assert.equal(validateSubmission({ ...good, score: '4210', orbs: '37' }).ok, true);
});

test('cleans names', () => {
  assert.equal(cleanName('  Star   Sailor '), 'Star Sailor');
  assert.equal(cleanName(''), null);
  assert.equal(cleanName('x'.repeat(17)), null);
  assert.equal(cleanName('<script>'), null);
  assert.equal(cleanName("Robert'); DROP TABLE"), null);
  assert.equal(cleanName(42), null);
});

test('rejects bad numbers', () => {
  assert.equal(validateSubmission({ ...good, score: -1 }).ok, false);
  assert.equal(validateSubmission({ ...good, score: 'lots' }).ok, false);
  assert.equal(validateSubmission({ ...good, duration: 0 }).ok, false);
  assert.equal(validateSubmission(null).ok, false);
});

test('rejects impossible runs', () => {
  // 10 km in 10 seconds is faster than the game's top speed
  assert.equal(validateSubmission({ ...good, distance: 10000, duration: 10 }).ok, false);
  // a million points from 3 orbs and 100 m
  assert.equal(validateSubmission({ ...good, score: 1_000_000, orbs: 3, distance: 100, duration: 30 }).ok, false);
});

test('clamps limit', () => {
  assert.equal(parseLimit(undefined), 10);
  assert.equal(parseLimit('5'), 5);
  assert.equal(parseLimit('999'), 50);
  assert.equal(parseLimit('-3'), 1);
  assert.equal(parseLimit('abc'), 10);
});
