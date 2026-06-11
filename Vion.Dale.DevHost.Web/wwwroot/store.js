// The one reactive store behind the DevHost Explorer. Owns config, live values, HAL contract
// values, connection state, collapse state, and all API calls. Components render from here and
// never talk to fetch/SignalR directly. SignalR pushes are coalesced (~100 ms) before they touch
// reactivity so event bursts don't cause render storms.

import { reactive } from './vue.esm-browser.prod.js';

export const store = reactive({
    loading: true,
    error: null,
    connected: false,
    config: null,
    topologyName: null,
    // `${serviceId}/${identifier}` -> last published property / measuring-point value
    values: {},
    // `${kind}/${spId}/${svcId}/${contractId}` -> last HAL contract value (kind: di/do/ai/ao)
    hal: {},
    // `${blockName}/${serviceIdentifier}/${groupKey}` -> explicit user collapse override (bool).
    // Name-path keyed (never per-run GUIDs) and persisted in localStorage.
    collapsed: {},
    selectedBlockId: null,
    // Bumped every 30 s so "relative" temporal cells re-render without a server push.
    relativeTick: 0,
    // Live filter query (topbar input, '/' shortcut). Matching policy: format.js matchesFilter.
    filter: '',
    // Pinned watch entries: { block, service, item } NAME paths (block name, service identifier,
    // item identifier — never per-run GUIDs). Persisted; unresolvable pins render as tombstones.
    pins: [],
    // Baseline diff: { values: snapshot } set by the topbar button / 'b'. Changed-since-baseline
    // drives amber dots, rail counters, and watch-tile deltas.
    baseline: null,
    baselineSeconds: 0,
});

const COLLAPSE_STORAGE_KEY = 'dale.devhost.collapsed';
const PINS_STORAGE_KEY = 'dale.devhost.pins';

export function valueKey(serviceId, identifier) {
    return `${serviceId}/${identifier}`;
}

export function halKey(kind, spId, svcId, contractId) {
    return `${kind}/${spId}/${svcId}/${contractId}`;
}

export function liveValue(serviceId, identifier) {
    return store.values[valueKey(serviceId, identifier)];
}

// ── Collapse state (policy in format.js: defaultOpen) ──────────────────────────

export function collapseKey(blockName, serviceIdentifier, groupKey) {
    return `${blockName}/${serviceIdentifier}/${groupKey}`;
}

export function toggleCollapsed(key, currentlyCollapsed) {
    store.collapsed[key] = !currentlyCollapsed;
    try {
        localStorage.setItem(COLLAPSE_STORAGE_KEY, JSON.stringify(store.collapsed));
    } catch (err) {
        console.warn('Could not persist collapse state', err);
    }
}

function loadCollapseState() {
    try {
        const raw = localStorage.getItem(COLLAPSE_STORAGE_KEY);
        if (raw) Object.assign(store.collapsed, JSON.parse(raw));
    } catch (err) {
        console.warn('Could not load collapse state', err);
    }
}

// ── Pins (watch panel) ──────────────────────────────────────────────────────────

export function pinKey(entry) {
    return `${entry.block}/${entry.service}/${entry.item}`;
}

export function isPinned(entry) {
    const key = pinKey(entry);
    return store.pins.some(p => pinKey(p) === key);
}

export function togglePin(entry) {
    const key = pinKey(entry);
    const idx = store.pins.findIndex(p => pinKey(p) === key);
    if (idx >= 0) store.pins.splice(idx, 1);
    else store.pins.push({ block: entry.block, service: entry.service, item: entry.item });
    try {
        localStorage.setItem(PINS_STORAGE_KEY, JSON.stringify(store.pins));
    } catch (err) {
        console.warn('Could not persist pins', err);
    }
}

function loadPins() {
    try {
        const raw = localStorage.getItem(PINS_STORAGE_KEY);
        if (raw) store.pins.push(...JSON.parse(raw));
    } catch (err) {
        console.warn('Could not load pins', err);
    }
}

// ── Baseline diff ───────────────────────────────────────────────────────────────

let baselineTimer = null;

export function setBaseline() {
    store.baseline = { values: JSON.parse(JSON.stringify(store.values)) };
    store.baselineSeconds = 0;
    clearInterval(baselineTimer);
    baselineTimer = setInterval(() => { store.baselineSeconds++; }, 1000);
}

export function clearBaseline() {
    store.baseline = null;
    store.baselineSeconds = 0;
    clearInterval(baselineTimer);
    baselineTimer = null;
}

export function changedSinceBaseline(key) {
    if (!store.baseline) return false;
    return JSON.stringify(store.values[key]) !== JSON.stringify(store.baseline.values[key]);
}

// Numeric delta vs the baseline, or null when either side isn't a number.
export function baselineDelta(key) {
    if (!store.baseline) return null;
    const now = store.values[key];
    const then = store.baseline.values[key];
    return typeof now === 'number' && typeof then === 'number' ? now - then : null;
}

// Changed-since-baseline count for a block (rail counters).
export function changedCountForBlock(lb) {
    if (!store.baseline) return 0;
    let n = 0;
    (lb.services || []).forEach(service => {
        [...(service.serviceProperties || []), ...(service.serviceMeasuringPoints || [])].forEach(item => {
            if (changedSinceBaseline(valueKey(service.id, item.identifier))) n++;
        });
    });
    return n;
}

// ── Errors ──────────────────────────────────────────────────────────────────────

export function showError(message) {
    store.error = String(message);
    setTimeout(() => {
        if (store.error === String(message)) store.error = null;
    }, 6000);
}

// ── API writes (all errors surface as toast; callers don't await unless they care) ──

export async function setProperty(serviceId, propertyId, value) {
    try {
        const response = await fetch(`/api/dale/property/${serviceId}/${propertyId}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ value }),
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
    } catch (err) {
        showError(`Failed to set ${propertyId}: ${err.message ?? err}`);
    }
}

export async function setDigitalInput(spId, svcId, contractId, value) {
    try {
        const response = await fetch(`/api/hal/di/${spId}/${svcId}/${contractId}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ value }),
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
    } catch (err) {
        showError(`Failed to set digital input: ${err.message ?? err}`);
    }
}

export async function setAnalogInput(spId, svcId, contractId, value) {
    try {
        const response = await fetch(`/api/hal/ai/${spId}/${svcId}/${contractId}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ value: parseFloat(value) }),
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
    } catch (err) {
        showError(`Failed to set analog input: ${err.message ?? err}`);
    }
}

// ── Coalesced SignalR apply ─────────────────────────────────────────────────────

const pendingValues = new Map();
const pendingHal = new Map();
let flushScheduled = false;

function scheduleFlush() {
    if (flushScheduled) return;
    flushScheduled = true;
    setTimeout(() => {
        flushScheduled = false;
        pendingValues.forEach((v, k) => { store.values[k] = v; });
        pendingValues.clear();
        pendingHal.forEach((v, k) => { store.hal[k] = v; });
        pendingHal.clear();
    }, 100);
}

function queueValue(serviceId, identifier, value) {
    pendingValues.set(valueKey(serviceId, identifier), value);
    scheduleFlush();
}

function queueHal(kind, spId, svcId, contractId, value) {
    pendingHal.set(halKey(kind, spId, svcId, contractId), value);
    scheduleFlush();
}

// ── Boot sequence: configuration → priming → SignalR ────────────────────────────

export async function initStore() {
    loadCollapseState();
    loadPins();
    setInterval(() => { store.relativeTick++; }, 30_000);
    try {
        const response = await fetch('/api/configuration');
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        store.config = await response.json();
        store.topologyName = store.config.topologyName || null;
        if (store.topologyName) {
            document.title = `DALE DevHost — ${store.topologyName}`;
        }
        store.loading = false;
    } catch (err) {
        store.loading = false;
        showError(`Failed to load configuration: ${err.message ?? err}`);
        return;
    }

    // Prime values from the REST snapshot BEFORE the SignalR connect so the page shows current
    // state immediately (no '-' placeholders until the connect burst). Wire dictionary keys are
    // camelCased while identifiers are PascalCase — lookup is case-insensitive. Null entries are
    // skipped: the cache can't distinguish "published null" from "never produced".
    await primeInitialValues();
    await connectHub();
}

async function primeInitialValues() {
    if (!store.config || !store.config.logicBlocks) return;
    await Promise.all(store.config.logicBlocks.map(async lb => {
        try {
            const response = await fetch(`/api/state/${encodeURIComponent(lb.id)}`);
            if (!response.ok) return;
            const state = await response.json();
            const byLowerKey = {};
            Object.keys(state).forEach(k => { byLowerKey[k.toLowerCase()] = state[k]; });
            (lb.services || []).forEach(service => {
                const all = [...(service.serviceProperties || []), ...(service.serviceMeasuringPoints || [])];
                all.forEach(item => {
                    const value = byLowerKey[item.identifier.toLowerCase()];
                    if (value !== null && value !== undefined) {
                        store.values[valueKey(service.id, item.identifier)] = value;
                    }
                });
            });
        } catch (err) {
            console.warn(`Failed to prime state for ${lb.name}:`, err);
        }
    }));
}

async function connectHub() {
    const connection = new window.signalR.HubConnectionBuilder()
        .withUrl('/hub')
        .withAutomaticReconnect()
        .build();

    connection.onreconnecting(() => { store.connected = false; });
    connection.onreconnected(() => { store.connected = true; });
    connection.onclose(() => { store.connected = false; });

    connection.on('PropertyValueChanged', d => queueValue(d.serviceIdentifier, d.propertyIdentifier, d.value));
    connection.on('MeasuringPointValueChanged', d => queueValue(d.serviceIdentifier, d.measuringPointIdentifier, d.value));
    connection.on('DigitalInputChanged', d => queueHal('di', d.serviceProviderIdentifier, d.serviceIdentifier, d.contractIdentifier, d.value));
    connection.on('DigitalOutputChanged', d => queueHal('do', d.serviceProviderIdentifier, d.serviceIdentifier, d.contractIdentifier, d.value));
    connection.on('AnalogInputChanged', d => queueHal('ai', d.serviceProviderIdentifier, d.serviceIdentifier, d.contractIdentifier, d.value));
    connection.on('AnalogOutputChanged', d => queueHal('ao', d.serviceProviderIdentifier, d.serviceIdentifier, d.contractIdentifier, d.value));

    try {
        await connection.start();
        store.connected = true;
    } catch (err) {
        showError(`Failed to connect to SignalR hub: ${err.message ?? err}`);
        store.connected = false;
    }
}

// ── Wiring lookups (ported from the monolith) ──────────────────────────────────

// SP endpoint key -> [{ lbId, lbName }] of all blocks mapping to it (shared-contract detection).
export function buildSharedContractLookup() {
    const lookup = {};
    if (!store.config || !store.config.logicBlocks) return lookup;
    store.config.logicBlocks.forEach(lb => {
        (lb.contractMappings || []).forEach(cm => {
            const key = `${cm.mappedServiceProviderIdentifier}/${cm.mappedServiceIdentifier}/${cm.mappedContractIdentifier}`;
            if (!lookup[key]) lookup[key] = [];
            lookup[key].push({ lbId: lb.id, lbName: lb.name });
        });
    });
    return lookup;
}

// Connections (interface mappings) touching a block, with direction arrows.
export function connectionsForLb(lbId) {
    const connections = [];
    if (!store.config || !store.config.interfaceMappings) return connections;
    store.config.interfaceMappings.forEach(m => {
        if (m.sourceLogicBlockId === lbId) {
            connections.push({
                arrow: '→',
                otherName: m.targetLogicBlockName,
                sourceIface: m.sourceInterfaceIdentifier,
                targetIface: m.targetInterfaceIdentifier,
                ownIface: m.sourceInterfaceIdentifier,
            });
        } else if (m.targetLogicBlockId === lbId) {
            connections.push({
                arrow: '←',
                otherName: m.sourceLogicBlockName,
                sourceIface: m.sourceInterfaceIdentifier,
                targetIface: m.targetInterfaceIdentifier,
                ownIface: m.targetInterfaceIdentifier,
            });
        }
    });
    return connections;
}
