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
    // Ctrl+K command palette (jump / pin).
    paletteOpen: false,
    // Run control: paused (timer/delayed fires held), canReset (supervisor attached).
    paused: false,
    canReset: false,
    // Top-level view: 'explorer' (default), 'topology', 'gallery', or 'player' (scenarios, RFC 0006).
    view: 'explorer',
    // Topology files (RFC 0006 R5): the discovery payload for the switcher in the topology panel.
    topologies: null,
    // Scenario surface (RFC 0006): the discovery payload, the opened scenario (parsed file), and the
    // latest run report. Run state lives SERVER-side (F5-safe, agent-visible) — the client only polls.
    scenarios: null,
    scenarioId: null,
    scenario: null,
    // The file byte-for-byte as served — the Player's "{ } scenario file" expander shows exactly
    // what is on disk, not a re-serialization.
    scenarioRaw: null,
    // Git blob hash of scenarioRaw — compared against the run report's fileHash so the Player can
    // flag "file changed since this run" after an edit + reload.
    scenarioFileHash: null,
    scenarioError: null,
    run: null,
    // Human judgment ticks, keyed `${runId}/${index}` -> 'ok' | 'notOk'. Local to this browser;
    // they enter the copied verification report, not the server.
    judgeTicks: {},
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

// Remove pins in bulk: all of them (the watch panel's clear), or a given subset (pruning
// tombstones after a topology switch). Pins are cheap to re-create — no confirmation ceremony.
export function clearPins(entries = null) {
    if (entries === null) {
        store.pins.splice(0, store.pins.length);
    } else {
        const keys = new Set(entries.map(pinKey));
        for (let i = store.pins.length - 1; i >= 0; i--) {
            if (keys.has(pinKey(store.pins[i]))) store.pins.splice(i, 1);
        }
    }
    try {
        localStorage.setItem(PINS_STORAGE_KEY, JSON.stringify(store.pins));
    } catch (err) {
        console.warn('Could not persist pins', err);
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

// ── Run control (pause / resume / reset) ────────────────────────────────────────

async function fetchControlStatus() {
    try {
        const response = await fetch('/api/control/status');
        if (!response.ok) return;
        const status = await response.json();
        store.paused = !!status.paused;
        store.canReset = !!status.canReset;
    } catch (err) {
        console.warn('Could not fetch control status', err);
    }
}

export async function pauseHost() {
    try {
        const response = await fetch('/api/control/pause', { method: 'POST' });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        store.paused = true;
    } catch (err) {
        showError(`Failed to pause: ${err.message ?? err}`);
    }
}

export async function resumeHost() {
    try {
        const response = await fetch('/api/control/resume', { method: 'POST' });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        store.paused = false;
    } catch (err) {
        showError(`Failed to resume: ${err.message ?? err}`);
    }
}

// Request a host recycle (dispose → rebuild → restart, same port). The server drops the SignalR
// connection while recycling; the onreconnected/onclose handlers rebuild the client state — no
// manual re-init here, so there is exactly one recovery path.
export async function resetHost() {
    try {
        const response = await fetch('/api/control/reset', { method: 'POST' });
        if (response.status === 409) {
            const body = await response.json();
            showError(body.error || 'Host is not supervised — reset unavailable.');
            return;
        }
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        store.connected = false;
        // The fresh generation has no run history — reflect "as if not run" immediately instead of
        // waiting for the reconnect to re-discover it.
        store.run = null;
    } catch (err) {
        showError(`Failed to reset: ${err.message ?? err}`);
    }
}

// After a host recycle EVERYTHING server-side is new — service ids, values, control state. The
// rendered config is stale the moment the connection blips, so both reconnect paths rebuild the
// whole client state; only the dead-connection path additionally creates a fresh hub connection.
let reinitInFlight = false;

async function reinitClientState() {
    if (reinitInFlight) return false;
    reinitInFlight = true;
    try {
        for (let attempt = 0; attempt < 60; attempt++) {
            try {
                const response = await fetch('/api/configuration');
                if (response.ok) {
                    store.config = await response.json();
                    store.topologyName = store.config.topologyName || null;
                    Object.keys(store.values).forEach(k => delete store.values[k]);
                    Object.keys(store.hal).forEach(k => delete store.hal[k]);
                    clearBaseline();
                    await primeInitialValues();
                    await fetchControlStatus();
                    // A recycled host has fresh (empty) scenario run state — re-discover, and drop a
                    // stale report from the previous generation. The topology list's "running" marker
                    // changes on a switch, so re-fetch that too.
                    await loadScenarios();
                    await loadTopologies();
                    store.run = null;
                    return true;
                }
            } catch {
                // Host still recycling — keep polling.
            }

            await new Promise(resolve => setTimeout(resolve, 1000));
        }

        showError('Host did not come back after reset — restart it manually.');
        return false;
    } finally {
        reinitInFlight = false;
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
    loadJudgeTicks();
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
    await fetchControlStatus();
    await loadScenarios();
    window.addEventListener('hashchange', applyHash);
    applyHash();
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

let hubConnection = null;

async function connectHub() {
    if (hubConnection) {
        try {
            await hubConnection.stop();
        } catch {
            // Already dead — that's why we're reconnecting.
        }
    }

    const connection = new window.signalR.HubConnectionBuilder()
        .withUrl('/hub')
        .withAutomaticReconnect()
        .build();
    hubConnection = connection;

    connection.onreconnecting(() => { store.connected = false; });
    // The connection survived (auto-reconnect) — but the host may have been RECYCLED meanwhile
    // (reset): every service id changed and the rendered config is stale. Rebuild the client
    // state; the existing connection stays.
    connection.onreconnected(() => {
        store.connected = true;
        reinitClientState();
    });
    // The connection is dead (auto-reconnect exhausted — e.g. a slow recycle): rebuild the client
    // state AND a fresh hub connection.
    connection.onclose(async () => {
        store.connected = false;
        if (connection !== hubConnection) return;
        if (await reinitClientState()) {
            await connectHub();
        }
    });

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

// ── Topology files (RFC 0006 R5) ────────────────────────────────────────────────

export async function loadTopologies() {
    try {
        const response = await fetch('/api/topologies');
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        store.topologies = await response.json();
    } catch (err) {
        console.warn('Could not list topologies', err);
    }
}

// Switching rides the reset: the server parks the topology id and recycles; the existing
// reconnect path rebuilds the whole client state against the new generation.
export async function switchTopology(id) {
    try {
        const response = await fetch(`/api/topologies/${encodeURIComponent(id)}/switch`, { method: 'POST' });
        if (response.status === 409) {
            const body = await response.json();
            showError(body.error || 'Topology switching needs a topology-aware supervisor.');
            return;
        }
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        store.connected = false;
        store.run = null;
    } catch (err) {
        showError(`Failed to switch topology: ${err.message ?? err}`);
    }
}

// ── Scenarios / Player (RFC 0006) ───────────────────────────────────────────────

const JUDGE_STORAGE_KEY = 'dale.devhost.judge';

export async function loadScenarios() {
    try {
        const response = await fetch('/api/scenarios');
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        store.scenarios = await response.json();
    } catch (err) {
        showError(`Failed to list scenarios: ${err.message ?? err}`);
    }
}

// Open one scenario in the Player: fetch the file (raw, byte-for-byte what's on disk — re-fetched
// on every open so IDE edits are picked up) and the latest run, then start polling. Staleness
// guards after every await: a slow fetch for a scenario the user already left must not clobber
// the newer one's state or kill its poll chain.
export async function openScenario(id) {
    store.view = 'player';
    store.scenarioId = id;
    store.scenario = null;
    store.scenarioRaw = null;
    store.scenarioError = null;
    store.run = null;
    if (location.hash !== `#/scenario/${id}`) location.hash = `#/scenario/${id}`;
    try {
        const response = await fetch(`/api/scenarios/${encodeURIComponent(id)}`);
        if (store.scenarioId !== id) return;
        if (!response.ok) throw new Error(response.status === 404 ? 'not found' : `HTTP ${response.status}`);
        const text = await response.text();
        if (store.scenarioId !== id) return;
        store.scenario = JSON.parse(text);
        store.scenarioRaw = text;
        store.scenarioFileHash = await gitBlobHash(text);
    } catch (err) {
        if (store.scenarioId === id) store.scenarioError = `Failed to load scenario '${id}': ${err.message ?? err}`;
        return;
    }
    await refreshRun(id);
    if (store.scenarioId === id) pollRun(id);
}

// Same formula the server uses (sha1 of "blob {len}\0" + bytes) — localhost is a secure context,
// so crypto.subtle is available. A BOM in the file can skew the client side (fetch strips it);
// worst case is a spurious "file changed" hint, never a missed one.
async function gitBlobHash(text) {
    try {
        const body = new TextEncoder().encode(text);
        const header = new TextEncoder().encode(`blob ${body.length}\0`);
        const buffer = new Uint8Array(header.length + body.length);
        buffer.set(header);
        buffer.set(body, header.length);
        const digest = await crypto.subtle.digest('SHA-1', buffer);
        return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, '0')).join('');
    } catch (err) {
        console.warn('Could not hash the scenario file', err);
        return null;
    }
}

export function closeScenario() {
    store.scenarioId = null;
    store.scenario = null;
    store.scenarioRaw = null;
    store.scenarioFileHash = null;
    store.scenarioError = null;
    store.run = null;
    loadScenarios();
    if (location.hash.startsWith('#/scenario/')) location.hash = '#/scenarios';
}

async function refreshRun(id) {
    try {
        const response = await fetch(`/api/scenarios/${encodeURIComponent(id)}/run`);
        if (response.status === 404) return;
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const report = await response.json();
        if (store.scenarioId === id) store.run = report;
    } catch (err) {
        console.warn('Could not fetch run status', err);
    }
}

let pollTimer = null;

// Poll the latest run while the scenario is open in the Player. Server-side run state makes this
// F5-safe: reload, re-open, and the poll re-attaches to whatever run is current (runId changes
// expose restarts). Fast cadence while a run is live; slow idle cadence otherwise, so runs
// started from another tab or an agent surface here too.
function pollRun(id) {
    clearTimeout(pollTimer);
    const live = store.run && store.run.status === 'running';
    pollTimer = setTimeout(async () => {
        if (store.scenarioId !== id || store.view !== 'player') return;
        await refreshRun(id);
        if (store.scenarioId === id && store.view === 'player') pollRun(id);
    }, live ? 400 : 2000);
}

export async function applyScenario(id, { restart = false, force = false } = {}) {
    try {
        const query = [restart ? 'restart=true' : null, force ? 'force=true' : null].filter(Boolean).join('&');
        const response = await fetch(`/api/scenarios/${encodeURIComponent(id)}/apply${query ? '?' + query : ''}`, { method: 'POST' });
        if (response.status === 409) {
            const body = await response.json();
            showError(body.error || 'A scenario run is already active.');
            return;
        }
        if (!response.ok) {
            // Surface the structured error body (e.g. the 422 list of format problems), not just the code.
            let detail = `HTTP ${response.status}`;
            try {
                const body = await response.json();
                detail = [body.error, ...(body.errors || [])].filter(Boolean).join(' · ') || detail;
            } catch {
                // No JSON body — keep the status code.
            }
            throw new Error(detail);
        }
        await refreshRun(id);
        pollRun(id);
    } catch (err) {
        showError(`Failed to start scenario: ${err.message ?? err}`);
    }
}

export function judgeKey(runId, index) {
    return `${runId}/${index}`;
}

export function setJudgeTick(runId, index, verdict) {
    const key = judgeKey(runId, index);
    if (store.judgeTicks[key] === verdict) delete store.judgeTicks[key];
    else store.judgeTicks[key] = verdict;
    try {
        localStorage.setItem(JUDGE_STORAGE_KEY, JSON.stringify(store.judgeTicks));
    } catch (err) {
        console.warn('Could not persist judgment ticks', err);
    }
}

function loadJudgeTicks() {
    try {
        const raw = localStorage.getItem(JUDGE_STORAGE_KEY);
        if (!raw) return;
        const parsed = JSON.parse(raw);
        // Keys are per-run GUIDs and would grow without bound; the ticks only matter for recent
        // runs (F5 survival), so reset the slate once it gets silly.
        if (Object.keys(parsed).length > 500) {
            localStorage.removeItem(JUDGE_STORAGE_KEY);
            return;
        }
        Object.assign(store.judgeTicks, parsed);
    } catch (err) {
        console.warn('Could not load judgment ticks', err);
    }
}

// Deep links (RFC 0006): #/scenario/{id} opens the Player on that scenario; #/scenarios opens the
// list. Applied on boot and on every hash change (back/forward navigation included).
function applyHash() {
    const match = /^#\/scenario\/([A-Za-z0-9._-]+)$/.exec(location.hash);
    if (match) {
        if (store.scenarioId !== match[1]) {
            openScenario(match[1]);
        } else {
            // Same scenario, e.g. returning from another view — restore the player and its poll chain.
            store.view = 'player';
            pollRun(match[1]);
        }
        return;
    }
    if (location.hash === '#/scenarios') {
        store.view = 'player';
        store.scenarioId = null;
        store.scenario = null;
        store.run = null;
        loadScenarios();
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
