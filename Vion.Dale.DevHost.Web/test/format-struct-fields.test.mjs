// Unit tests for the struct-field presentation policy in format.js — the helpers the read-only
// StructViewer and the flat-struct form both read, so the two surfaces cannot drift. Dev-time only
// (`node --test Vion.Dale.DevHost.Web/test/format-struct-fields.test.mjs`); sits above wwwroot so
// the embed glob never bundles it.
//
// formatTemporal / parseDurationToMs reach for the global `window.dayjs` the page installs from the
// vendored classic scripts, so the tests install the same vendored files into a fake window first.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const wwwroot = fileURLToPath(new URL('../wwwroot/', import.meta.url));

function installVendoredDayjs() {
    const sandbox = { window: {} };
    sandbox.globalThis = sandbox;
    sandbox.self = sandbox;
    vm.createContext(sandbox);
    for (const file of ['dayjs.min.js', 'dayjs.duration.min.js', 'dayjs.relativeTime.min.js', 'dayjs.localizedFormat.min.js']) {
        vm.runInContext(readFileSync(wwwroot + file, 'utf8'), sandbox);
    }
    sandbox.dayjs.extend(sandbox.dayjs_plugin_duration);
    sandbox.dayjs.extend(sandbox.dayjs_plugin_relativeTime);
    sandbox.dayjs.extend(sandbox.dayjs_plugin_localizedFormat);
    return sandbox.dayjs;
}

globalThis.window = { dayjs: installVendoredDayjs() };

const {
    resolveFieldLabel, temporalFieldFormat, stringFieldFormat, formatFieldValue,
    parseDurationInput, msToIso8601Duration,
} = await import('../wwwroot/format.js');

// ── labels ──────────────────────────────────────────────────────────────────────

test('resolveFieldLabel prefers the authored [StructField] Title', () => {
    assert.equal(resolveFieldLabel('queueDepth', { type: 'integer', title: 'Queue depth' }), 'Queue depth');
});

test('resolveFieldLabel falls back to the wire name for an enum field, whose title is its type name', () => {
    // The SDK puts the CLR enum type name in title; the authored Title is dropped (struct fields
    // have no presentation channel to route it to). "ModbusLinkState" is not a field label.
    const enumField = { type: 'string', title: 'ModbusLinkState', enum: ['Unknown', 'Online', 'Faulted'] };
    assert.equal(resolveFieldLabel('state', enumField), 'state');
});

test('resolveFieldLabel falls back to the wire name when there is no title at all', () => {
    assert.equal(resolveFieldLabel('successCount', { type: 'integer' }), 'successCount');
    assert.equal(resolveFieldLabel('successCount', null), 'successCount');
});

// ── formats ─────────────────────────────────────────────────────────────────────

test('temporalFieldFormat picks out duration and date-time only', () => {
    assert.equal(temporalFieldFormat({ format: 'duration' }), 'duration');
    assert.equal(temporalFieldFormat({ format: 'date-time' }), 'date-time');
    assert.equal(temporalFieldFormat({ format: 'ipv4' }), null);
    assert.equal(temporalFieldFormat({ format: 'int64' }), null);
    assert.equal(temporalFieldFormat({}), null);
});

test('stringFieldFormat surfaces an advisory string format but never a temporal or numeric one', () => {
    assert.equal(stringFieldFormat({ type: 'string', format: 'ipv4' }), 'ipv4');
    assert.equal(stringFieldFormat({ type: ['string', 'null'], format: 'ipv4' }), 'ipv4');
    assert.equal(stringFieldFormat({ type: ['string', 'null'], format: 'duration' }), null);
    assert.equal(stringFieldFormat({ type: 'integer', format: 'int64' }), null);
});

// ── values ──────────────────────────────────────────────────────────────────────

test('formatFieldValue scales a duration to its magnitude instead of flattening it to 00:00:00', () => {
    const dur = { type: ['string', 'null'], format: 'duration' };
    // The Modbus case: a device that answers in 0.2 ms must not read as "00:00:00".
    assert.equal(formatFieldValue('PT0.0002S', dur), '0.2 ms');
    assert.equal(formatFieldValue('PT0.9S', dur), '900 ms');
    assert.equal(formatFieldValue('PT3S', dur), '3 s');
    assert.equal(formatFieldValue('PT1.5S', dur), '1.5 s');
    assert.equal(formatFieldValue('PT1M30S', dur), '00:01:30');
    assert.equal(formatFieldValue('PT0S', dur), '0 ms');
});

test('formatFieldValue renders a date-time field through the temporal formatter', () => {
    const shown = formatFieldValue('2026-08-19T10:00:00Z', { type: ['string', 'null'], format: 'date-time' });
    assert.notEqual(shown, '2026-08-19T10:00:00Z');
    assert.match(shown, /2026/);
});

test('formatFieldValue leaves non-temporal values to formatValue, and nulls read as an em dash', () => {
    assert.equal(formatFieldValue(42, { type: 'integer' }), '42');
    assert.equal(formatFieldValue(0.12345, { type: 'number' }), '0.123');
    assert.equal(formatFieldValue('Online', { type: 'string', enum: ['Online'] }), 'Online');
    assert.equal(formatFieldValue(null, { type: 'integer' }), '—');
    assert.equal(formatFieldValue(undefined, { type: 'integer' }), '—');
});

// ── duration input ──────────────────────────────────────────────────────────────

test('parseDurationInput accepts the ISO-8601 wire form unchanged in meaning', () => {
    assert.equal(parseDurationInput('PT3S'), 'PT3S');
    assert.equal(parseDurationInput('PT1M30S'), 'PT1M30S');
    assert.equal(parseDurationInput('P1DT2H'), 'P1DT2H');
});

test('parseDurationInput accepts the .NET TimeSpan form', () => {
    assert.equal(parseDurationInput('00:00:03'), 'PT3S');
    assert.equal(parseDurationInput('00:01:30'), 'PT1M30S');
    assert.equal(parseDurationInput('1.02:00:00'), 'P1DT2H');
});

test('parseDurationInput accepts the shorthand a human actually types', () => {
    assert.equal(parseDurationInput('3s'), 'PT3S');
    assert.equal(parseDurationInput('1.5s'), 'PT1.5S');
    assert.equal(parseDurationInput('500ms'), 'PT0.5S');
    assert.equal(parseDurationInput('2m'), 'PT2M');
    assert.equal(parseDurationInput('1h 30m'), 'PT1H30M');
    assert.equal(parseDurationInput('1d'), 'P1D');
    assert.equal(parseDurationInput(' 250 ms '), 'PT0.25S');
});

test('parseDurationInput returns null for anything it cannot read, so the caller can refuse the write', () => {
    assert.equal(parseDurationInput('garbage'), null);
    assert.equal(parseDurationInput('3 fortnights'), null);
    assert.equal(parseDurationInput('3s and then some'), null);
    assert.equal(parseDurationInput(''), null);
    assert.equal(parseDurationInput('   '), null);
    assert.equal(parseDurationInput(null), null);
    assert.equal(parseDurationInput(5), null);
});

test('msToIso8601Duration mirrors the shape XmlConvert.ToString writes', () => {
    assert.equal(msToIso8601Duration(0), 'PT0S');
    assert.equal(msToIso8601Duration(3000), 'PT3S');
    assert.equal(msToIso8601Duration(90000), 'PT1M30S');
    assert.equal(msToIso8601Duration(86400000), 'P1D');
    assert.equal(msToIso8601Duration(93600000), 'P1DT2H');
    assert.equal(msToIso8601Duration(-3000), '-PT3S');
});
