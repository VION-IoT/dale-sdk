import { test } from 'node:test';
import assert from 'node:assert/strict';
import { traceSeriesFor, signTone, traceLaneKind } from '../wwwroot/format.js';

const watchTrace = [
    { phase: 'start', stepIndex: -1, virtualElapsedMs: 0, values: { 'refGridMeter.ComputedGridActivePowerKw': null, 'energyManager.OffGridActive': false } },
    { phase: 'steps', stepIndex: 0, virtualElapsedMs: 0, values: { 'refGridMeter.ComputedGridActivePowerKw': null, 'energyManager.OffGridActive': false } },
    { phase: 'steps', stepIndex: 1, virtualElapsedMs: 4200, values: { 'refGridMeter.ComputedGridActivePowerKw': 2.86, 'energyManager.OffGridActive': true } },
];

test('traceSeriesFor joins a PascalCase path to camelCased wire keys', () => {
    const series = traceSeriesFor(watchTrace, 'RefGridMeter.ComputedGridActivePowerKw');
    assert.deepEqual(series.map(s => s.value), [null, null, 2.86]);
    assert.deepEqual(series.map(s => s.stepIndex), [-1, 0, 1]);
    assert.equal(series[2].virtualElapsedMs, 4200);
});

test('traceSeriesFor returns undefined values for an unknown path', () => {
    const series = traceSeriesFor(watchTrace, 'Nope.Missing');
    assert.deepEqual(series.map(s => s.value), [undefined, undefined, undefined]);
});

test('traceSeriesFor tolerates a missing/empty trace', () => {
    assert.deepEqual(traceSeriesFor(null, 'X.Y'), []);
    assert.deepEqual(traceSeriesFor([], 'X.Y'), []);
});

test('signTone classifies positive, negative, and zero/non-numeric', () => {
    assert.equal(signTone(2.86), 'pos');
    assert.equal(signTone(-1.9), 'neg');
    assert.equal(signTone(0), 'zero');
    assert.equal(signTone(null), 'zero');
    assert.equal(signTone('Applied'), 'zero');
});

test('traceLaneKind classifies by schema, then by sample value', () => {
    assert.equal(traceLaneKind({ type: 'number' }), 'numeric');
    assert.equal(traceLaneKind({ type: ['integer', 'null'] }), 'numeric');
    assert.equal(traceLaneKind({ type: 'boolean' }), 'state');
    assert.equal(traceLaneKind({ type: 'string', enum: ['Calm', 'Busy'] }), 'state');
    assert.equal(traceLaneKind({ type: 'object' }), 'struct');
    assert.equal(traceLaneKind({ type: 'array' }), 'struct');
    assert.equal(traceLaneKind(null, 2.86), 'numeric');
    assert.equal(traceLaneKind(null, 'Applied'), 'state');
    assert.equal(traceLaneKind(null, { l1: 0 }), 'struct');
    assert.equal(traceLaneKind(null, null), 'state');
});
