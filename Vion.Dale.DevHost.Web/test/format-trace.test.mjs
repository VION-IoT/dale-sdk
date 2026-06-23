import { test } from 'node:test';
import assert from 'node:assert/strict';
import { traceSeriesFor, signTone, traceLaneKind } from '../wwwroot/format.js';
import { stepRibbonGeometry, sampleX, traceNumericBand, traceStateBands } from '../wwwroot/format.js';

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

test('traceSeriesFor prefers an exact key over a case-variant', () => {
    const trace = [{ stepIndex: 0, virtualElapsedMs: 0, values: { 'a.B': 1, 'A.B': 2 } }];
    assert.equal(traceSeriesFor(trace, 'A.B')[0].value, 2);
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

const steps = [
    { index: 0, kind: 'set', label: 'stage', status: 'ok', virtualElapsedMs: 0 },
    { index: 1, kind: 'waitUntil', label: 'converge', status: 'ok', virtualElapsedMs: 4200 },
    { index: 2, kind: 'waitUntil', label: 'applied', status: 'ok', virtualElapsedMs: 0 },
];

test('stepRibbonGeometry spaces steps by virtual duration with a minimum width', () => {
    const g = stepRibbonGeometry(steps, { minFrac: 0.1 });
    assert.equal(g.axis, 'virtual');
    assert.equal(g.steps.length, 3);
    assert.equal(g.steps[0].x0, 0);
    assert.ok(Math.abs(g.steps[2].x1 - 1) < 1e-9, 'segments fill 0..1');
    const w = s => s.x1 - s.x0;
    assert.ok(w(g.steps[1]) > w(g.steps[0]));
    assert.ok(w(g.steps[1]) > w(g.steps[2]));
    assert.ok(w(g.steps[0]) >= 0.1 - 1e-9 && w(g.steps[2]) >= 0.1 - 1e-9);
});

test('stepRibbonGeometry falls back to equal columns with no timing', () => {
    const g = stepRibbonGeometry([{ index: 0 }, { index: 1 }], { minFrac: 0 });
    assert.equal(g.axis, 'index');
    assert.ok(Math.abs((g.steps[1].x1 - g.steps[1].x0) - (g.steps[0].x1 - g.steps[0].x0)) < 1e-9);
});

test('sampleX maps a watchTrace sample stepIndex to an x position', () => {
    const g = stepRibbonGeometry(steps, { minFrac: 0.1 });
    assert.equal(sampleX(g, -1), 0);
    assert.equal(sampleX(g, 1), g.steps[1].x1);
    assert.equal(sampleX(g, 99), 1);
});

test('traceNumericBand includes zero so the baseline is sign-aware', () => {
    assert.deepEqual(traceNumericBand([{ value: 2 }, { value: 3 }, { value: null }]), { min: 0, max: 3, zero: 0 });
    assert.deepEqual(traceNumericBand([{ value: -1.9 }, { value: -0.5 }]), { min: -1.9, max: 0, zero: 0 });
    const b = traceNumericBand([{ value: 0 }, { value: 0 }]);
    assert.ok(b.min < 0 && b.max > 0);
});

test('traceStateBands merges consecutive equal values into segments', () => {
    const g = stepRibbonGeometry(steps, { minFrac: 0.1 });
    const series = [
        { stepIndex: -1, value: 'Inactive' },
        { stepIndex: 0, value: 'Inactive' },
        { stepIndex: 1, value: 'Applied' },
        { stepIndex: 2, value: 'Applied' },
    ];
    const bands = traceStateBands(series, g);
    assert.equal(bands.length, 2);
    assert.equal(bands[0].value, 'Inactive');
    assert.equal(bands[1].value, 'Applied');
    assert.equal(bands[0].x0, 0);
    assert.ok(Math.abs(bands[1].x1 - 1) < 1e-9);
});
