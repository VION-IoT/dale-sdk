// The one reactive store behind the DevHost Explorer. Owns config, live values, HAL contract
// values, connection state, collapse state, and all API calls. Components render from here and
// never talk to fetch/SignalR directly. SignalR pushes are coalesced (~100 ms) before they touch
// reactivity so event bursts don't cause render storms.

import { reactive } from './vue.esm-browser.prod.js';
import { scenarioErrors } from './scenario-forms.js';

export const store = reactive({
    loading: true,
    error: null,
    connected: false,
    // A host recycle (topology switch / reset / recycle-on-run) is in flight — the workspace shows a
    // determinate "recycling…" busy state until the fresh generation answers (cleared in reinitClientState).
    recycling: false,
    config: null,
    topologyName: null,
    // `${serviceId}/${identifier}` -> last published property / measuring-point value
    values: {},
    // `${serviceId}/${identifier}` -> recent numeric samples (ring buffer, newest last) for inline
    // metric sparklines. Only changed values push (Metalama [Observable] dedups exact-equal), so a flat
    // metric accrues no trend — which is exactly when a sparkline should stay hidden (needs ≥2 samples).
    history: {},
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
    // The '?' keyboard-shortcuts help overlay.
    helpOpen: false,
    // Run control: paused (timer/delayed fires held), canReset (supervisor attached),
    // stepped (deterministic virtual clock — scenario runs step exactly; `dale dev --stepped`).
    paused: false,
    canReset: false,
    stepped: false,
    // True while a scenario run owns the virtual clock (stepped mode only); clears when the run
    // reaches a terminal status or fetchControlStatus resyncs. Manual stepping is blocked while true.
    runActive: false,
    // The host's virtual clock (ISO string), shown in the stepped-mode control cluster.
    virtualTime: null,
    // Top-level view: 'explorer' (default), 'topology', 'gallery', or 'player' (scenarios, RFC 0006).
    view: 'explorer',
    // Topology files (RFC 0006 R5): the discovery payload for the switcher in the topology panel.
    topologies: null,
    // Topology authoring (RFC 0013): logic-block definitions (the palette + wiring source of truth) and
    // the in-progress topology draft the editor mutates. Draft is null when no editor is open.
    definitions: [],
    topologyDraft: null,
    topologyDraftDirty: false,
    topologyDraftErrors: [],
    // Topology panel screen state (RFC 0013): a scenario-style master→detail→editor router. One of
    // 'list' (the file picker), 'detail' (a read-only render of a selected file), or 'editor' (the
    // draft editor). Lives in the STORE (not a local ref) so an external requester (⌘K palette /
    // Shift+T) that navigates BEFORE the panel mounts still lands on the right screen — the panel reads
    // the state at render time, mount-order-independent. The requester sets the view first.
    topologyScreen: 'list',
    // The file id the Detail screen is showing / the Editor was opened from (so closing the editor
    // returns to that file's Detail). Null on the List screen and for a brand-new draft.
    topologySelectedId: null,
    // The fetched topology file object (GET /api/topologies/{id}) rendered read-only on the Detail screen.
    topologyDetail: null,
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
    // Recycle-on-run (RFC 0008): when an apply recycles the host onto the scenario's topology for a clean
    // slate, the run is parked here and re-applied once the fresh generation is up (in reinitClientState).
    pendingScenarioRun: null,
    // Human judgment ticks, keyed `${runId}/${index}` -> 'ok' | 'notOk'. Local to this browser;
    // they enter the copied verification report, not the server.
    judgeTicks: {},
    // Scenario authoring (RFC 0014): the in-progress draft + the Verify editor screen flag. scenarioScreen
    // 'detail' (read-only run view) | 'editor' (the form editor); null draft when no editor is open.
    scenarioScreen: 'detail',
    scenarioDraft: null,
    scenarioDraftDirty: false,
    scenarioDraftErrors: [],
});

const COLLAPSE_STORAGE_KEY = 'dale.devhost.collapsed';
const PINS_STORAGE_KEY = 'dale.devhost.pins';

export function valueKey(serviceId, identifier) {
    return `${serviceId}/${identifier}`;
}

export function halKey(spId, svcId, contractId) {
    return `${spId}/${svcId}/${contractId}`;
}

export function liveValue(serviceId, identifier) {
    return store.values[valueKey(serviceId, identifier)];
}

// ── Metric history (inline sparklines) ──────────────────────────────────────────

const HISTORY_CAP = 40;

// Append a numeric sample to a value's ring buffer. Non-numbers / non-finite are ignored, so a status
// enum, string, or struct never grows a series. Newest last, capped.
function recordHistory(key, value) {
    if (typeof value !== 'number' || !Number.isFinite(value)) return;
    let series = store.history[key];
    if (!series) {
        series = [];
        store.history[key] = series;
    }
    series.push(value);
    if (series.length > HISTORY_CAP) series.shift();
}

export function historyFor(serviceId, identifier) {
    return store.history[valueKey(serviceId, identifier)] || null;
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

// Move a pinned entry one slot up (dir = -1) or down (dir = +1) and persist. No-ops at the
// boundary: the caller is responsible for disabling the button at index 0 (up) and the last index
// (down), but a no-op here is safe in case of a race.
export function movePinAt(index, dir) {
    const target = index + dir;
    if (target < 0 || target >= store.pins.length) return;
    const [removed] = store.pins.splice(index, 1);
    store.pins.splice(target, 0, removed);
    try {
        localStorage.setItem(PINS_STORAGE_KEY, JSON.stringify(store.pins));
    } catch (err) {
        console.warn('Could not persist pins', err);
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

// Changed-since-baseline count for a block (rail counters). A member that is both a service property and
// a measuring point shares one value key — count it once, matching the deduped render (format.groupItems).
export function changedCountForBlock(lb) {
    if (!store.baseline) return 0;
    let n = 0;
    (lb.services || []).forEach(service => {
        const seen = new Set();
        [...(service.serviceProperties || []), ...(service.serviceMeasuringPoints || [])].forEach(item => {
            const key = valueKey(service.id, item.identifier);
            if (seen.has(key)) return;
            seen.add(key);
            if (changedSinceBaseline(key)) n++;
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

// Drive any value-contract input (RFC 0010): one generic endpoint for every contract type. The caller builds
// the wire `value` from the rendered control — a bool for a digital toggle, a number for an analog field — and
// `handlerName` is the contract's stand-in actor name (its contractHandlerActorName annotation).
export async function driveContract(handlerName, spId, svcId, contractId, value) {
    try {
        const response = await fetch(`/api/contracts/drive/${handlerName}/${spId}/${svcId}/${contractId}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ value }),
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
    } catch (err) {
        showError(`Failed to drive contract: ${err.message ?? err}`);
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
        store.stepped = !!status.stepped;
        store.virtualTime = status.virtualTimeUtc ?? null;
        store.runActive = !!status.runActive;
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

// Manual stepping (RFC 0008 §Part 4): drive the virtual clock by hand on a stepped host. The server
// 409s when not stepped or while a scenario run is driving the clock — surfaced as an error toast.
export async function stepHost() {
    await driveClock('/api/control/step', 'step');
}

export async function advanceHost(seconds) {
    await driveClock(`/api/control/advance?seconds=${seconds}`, `advance ${seconds}s`);
}

async function driveClock(url, label) {
    try {
        const response = await fetch(url, { method: 'POST' });
        if (!response.ok) {
            const body = await response.json().catch(() => ({}));
            throw new Error(body.error ?? `HTTP ${response.status}`);
        }
        const body = await response.json();
        if (body.virtualTimeUtc) store.virtualTime = body.virtualTimeUtc;
    } catch (err) {
        showError(`Failed to ${label}: ${err.message ?? err}`);
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
        store.recycling = true;
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
                    Object.keys(store.history).forEach(k => delete store.history[k]);
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
                    // Recycle-on-run: if a scenario apply triggered this recycle, run it now on the fresh,
                    // clean generation — but ONLY once the host is actually on the scenario's topology. A
                    // transient reconnect to the OLD generation (still tearing down) would otherwise re-apply
                    // against the wrong topology and re-trigger the recycle in a loop, so keep the pending run
                    // parked until the target topology lands. Then the re-apply runs in place (epoch clock,
                    // matching topology — no second recycle).
                    const pending = store.pendingScenarioRun;
                    if (pending && store.scenarioId === pending.id && store.view === 'player' && store.scenario && store.topologyName === store.scenario.topology) {
                        store.pendingScenarioRun = null;
                        applyScenario(pending.id, { restart: pending.restart });
                    }

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
        store.recycling = false;
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
        pendingValues.forEach((v, k) => { store.values[k] = v; recordHistory(k, v); });
        pendingValues.clear();
        pendingHal.forEach((v, k) => { store.hal[k] = v; });
        pendingHal.clear();
    }, 100);
}

function queueValue(serviceId, identifier, value) {
    pendingValues.set(valueKey(serviceId, identifier), value);
    scheduleFlush();
}

function queueHal(spId, svcId, contractId, value) {
    pendingHal.set(halKey(spId, svcId, contractId), value);
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
    // Q3 re-entry: editScenarioDraft on a foreign topology parks the edit intent then recycles via a full
    // page reload (switchTopology). We're now booted on the (matching) topology — consume the parked intent
    // and re-enter the editor. Runs after store.topologyName is set + applyHash, so editScenarioDraft sees
    // file.topology === store.topologyName and does NOT recycle again.
    consumeEditAfterRecycle();
    await connectHub();
}

// Read + clear the parked Q3 edit intent and re-open the editor on it. Best-effort: a missing/garbled key
// or unavailable sessionStorage is a no-op (worst case the user re-clicks Edit on the landed topology).
function consumeEditAfterRecycle() {
    let parked = null;
    try {
        parked = sessionStorage.getItem('dale.scenario.editAfterRecycle');
        sessionStorage.removeItem('dale.scenario.editAfterRecycle');
    } catch { return; }
    if (!parked) return;
    try {
        const intent = JSON.parse(parked);
        if (intent && intent.id) editScenarioDraft(intent.id, { asClone: !!intent.asClone });
    } catch { /* garbled park — ignore */ }
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
                        const key = valueKey(service.id, item.identifier);
                        store.values[key] = value;
                        recordHistory(key, value);
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
    connection.on('ServiceProviderContractChanged', d => queueHal(d.serviceProviderIdentifier, d.serviceIdentifier, d.contractIdentifier, d.value));

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

export async function loadDefinitions() {
    try {
        const response = await fetch('/api/logic-block-definitions');
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        store.definitions = (await response.json()).definitions || [];
    } catch (err) { console.warn('Could not list logic-block definitions', err); }
}

export function newTopologyDraft() {
    store.topologyDraft = { id: '', logicBlockInstances: [], interfaceMappings: [], contractMappings: [] };
    store.topologyDraftDirty = false; store.topologyDraftErrors = [];
    loadDefinitions();
}
export async function cloneTopologyDraft(id) {
    await loadDefinitions();
    try {
        const res = await fetch(`/api/topologies/${encodeURIComponent(id)}`);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const file = await res.json();
        store.topologyDraft = { id: file.id || id, logicBlockInstances: file.logicBlockInstances || [], interfaceMappings: file.interfaceMappings || [], contractMappings: file.contractMappings || [] };
        store.topologyDraftDirty = false; store.topologyDraftErrors = [];
    } catch (err) { showError(`Could not load topology '${id}': ${err.message ?? err}`); }
}
export async function validateTopologyDraft() {
    if (!store.topologyDraft) return false;
    try {
        const res = await fetch('/api/topologies/validate', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(store.topologyDraft) });
        if (res.ok) { store.topologyDraftErrors = []; return true; }
        const body = await res.json().catch(() => ({}));
        store.topologyDraftErrors = body.errors || ['validation failed']; return false;
    } catch (err) { store.topologyDraftErrors = [String(err.message ?? err)]; return false; }
}
export async function saveTopologyDraft() {
    if (!store.topologyDraft || !store.topologyDraft.id) { store.topologyDraftErrors = ['id is required']; return false; }
    try {
        const savedId = store.topologyDraft.id;
        const res = await fetch(`/api/topologies/${encodeURIComponent(savedId)}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(store.topologyDraft) });
        if (res.ok) {
            store.topologyDraftDirty = false; store.topologyDraftErrors = [];
            // Land on the just-saved file's Detail screen (loadTopologies runs inside openTopologyDetail) —
            // so a fresh save is immediately reviewable, with an Edit button right there.
            await openTopologyDetail(savedId);
            return true;
        }
        if (res.status === 403) { const b = await res.json().catch(() => ({})); showError(b.error || 'topology saving is disabled'); return false; }
        const body = await res.json().catch(() => ({})); store.topologyDraftErrors = body.errors || ['save failed']; return false;
    } catch (err) { store.topologyDraftErrors = [String(err.message ?? err)]; return false; }
}

// ── Topology panel navigation (RFC 0013): the list → detail → editor master-detail flow ──────────
// All screen transitions + their I/O live here so the components are pure renders of store state.

export async function openTopologyList() {
    store.topologyScreen = 'list';
    store.topologySelectedId = null;
    await loadTopologies();
}

// Fetch a file and show it read-only on the Detail screen. loadTopologies first so the "running"
// marker + read-only gate the screen reads are current.
export async function openTopologyDetail(id) {
    await loadTopologies();
    try {
        const res = await fetch(`/api/topologies/${encodeURIComponent(id)}`);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        store.topologyDetail = await res.json();
        store.topologySelectedId = id;
        store.topologyScreen = 'detail';
    } catch (err) {
        showError(`Could not load topology '${id}': ${err.message ?? err}`);
    }
}

// Edit a file in place: clone its body into the draft under the SAME id, so Save overwrites the file.
export async function editTopology(id) {
    await cloneTopologyDraft(id);
    store.topologyScreen = 'editor';
}

// Clone a file into a NEW file: same body, blank id — Save creates a new file once the author names it.
export async function cloneTopology(id) {
    await cloneTopologyDraft(id);
    if (store.topologyDraft) store.topologyDraft.id = '';
    store.topologyDraftDirty = true;
    store.topologyScreen = 'editor';
}

// Author a fresh, empty topology.
export function newTopology() {
    newTopologyDraft();
    store.topologySelectedId = null;
    store.topologyScreen = 'editor';
}

// Author a topology from clipboard JSON: mirrors cloneTopologyDraft's draft-building (so the shape
// matches what the form/raw editor expects) and newTopology's navigation, landing on the form view
// so the author reviews before Save — this does not save or catalog-validate anything.
export async function pasteTopology() {
    let text;
    try {
        text = await navigator.clipboard.readText();
    } catch (err) {
        showError(`Could not read the clipboard: ${err.message ?? err}`);
        return;
    }
    if (!text || !text.trim()) { showError('Clipboard is empty.'); return; }
    let parsed;
    try {
        parsed = JSON.parse(text);
    } catch (e) {
        showError('Clipboard does not contain valid topology JSON: ' + e.message);
        return;
    }
    await loadDefinitions();
    store.topologyDraft = {
        id: parsed.id || '',
        logicBlockInstances: parsed.logicBlockInstances || [],
        interfaceMappings: parsed.interfaceMappings || [],
        contractMappings: parsed.contractMappings || [],
    };
    store.topologyDraftDirty = true;
    store.topologyDraftErrors = [];
    store.topologySelectedId = null;
    store.topologyScreen = 'editor';
}

// Leave the editor: clear the draft + dirty/errors first (mirrors closeScenarioEditor — so a later
// re-entry always rebuilds rather than reading stale draft state), then back to the file's Detail if one
// was selected (edit/clone-in-place), else the list.
export function closeTopologyEditor() {
    store.topologyDraft = null; store.topologyDraftDirty = false; store.topologyDraftErrors = [];
    if (store.topologySelectedId) openTopologyDetail(store.topologySelectedId);
    else openTopologyList();
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
        store.recycling = true;
        store.run = null;
        // Land on a correctly-primed Explore view of the new topology. A switch recycles the whole host
        // (new actor system, new service ids); the soft SignalR reconnect does not reliably re-prime the
        // live value stream against that fresh generation — the workspace shows the new structure but
        // blank values until a manual reload. A switch is heavy and user-initiated, so once the fresh host
        // is actually serving the target topology we hard-reload to '/': a clean boot primes values from
        // the REST snapshot over a fresh hub subscription, and the default Explore view is exactly where
        // the user wants to land after "switch & run".
        for (let attempt = 0; attempt < 120; attempt++) {
            try {
                const probe = await fetch('/api/configuration');
                if (probe.ok) {
                    const cfg = await probe.json();
                    if ((cfg.topologyName || null) === id) {
                        if (location.hash) location.hash = '';
                        window.location.assign('/');
                        return;
                    }
                }
            } catch {
                // Host still recycling — keep polling.
            }
            await new Promise(resolve => setTimeout(resolve, 500));
        }
        showError('Host did not come back on the new topology — reload the page manually.');
    } catch (err) {
        showError(`Failed to switch topology: ${err.message ?? err}`);
    }
}

// Switch the host's clock mode (stepped ⇄ real, RFC 0012 §4): rebuilds the host in the other mode,
// riding the same recycle as a topology switch (the reconnect path rebuilds the client state).
export async function switchClockMode(stepped) {
    try {
        const response = await fetch(`/api/control/clock-mode?stepped=${stepped}`, { method: 'POST' });
        if (response.status === 409) {
            const body = await response.json().catch(() => ({}));
            showError(body.error || 'Clock-mode switching needs a supervised host.');
            return;
        }
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        store.connected = false;
        store.recycling = true;
        store.run = null;
    } catch (err) {
        showError(`Failed to switch clock mode: ${err.message ?? err}`);
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
    store.runActive = false;
    loadScenarios();
    if (location.hash.startsWith('#/scenario/')) location.hash = '#/scenarios';
}

// Author a brand-new scenario for the running topology (Q3: topology is locked to what's loaded).
export function newScenarioDraft() {
    store.scenarioDraft = { version: 1, id: '', topology: store.topologyName || '', setup: [], steps: [], watch: [], judge: [] };
    store.scenarioDraftDirty = false; store.scenarioDraftErrors = [];
    store.view = 'player'; store.scenarioScreen = 'editor';
}

// Edit / clone an existing scenario. Q3: if its topology != the running one, recycle onto it first so the
// editor's pickers/values are for the right rig. `asClone` blanks the id (a new file).
export async function editScenarioDraft(id, { asClone = false } = {}) {
    const res = await fetch(`/api/scenarios/${encodeURIComponent(id)}`);
    if (!res.ok) { showError(`Could not load scenario '${id}'`); return; }
    const file = JSON.parse(await res.text());
    if (file.topology && file.topology !== store.topologyName) {
        // Q3: switchTopology recycles onto the scenario's topology with a FULL PAGE RELOAD (window.location
        // .assign('/')), so the edit intent is lost across the reload. Park it in sessionStorage; initStore
        // consumes it on boot once the host is on the matching topology, re-entering the editor (no second
        // recycle). See the consumeEditAfterRecycle hook in initStore.
        try { sessionStorage.setItem('dale.scenario.editAfterRecycle', JSON.stringify({ id, asClone })); } catch { /* sessionStorage unavailable — re-entry is best-effort */ }
        await switchTopology(file.topology);   // recycles + reloads onto the scenario's topology (RFC 0013)
        return;                                 // the reload re-enters the editor fresh; see Task 7 deep-link note
    }
    store.scenarioDraft = { ...file, id: asClone ? '' : file.id, setup: file.setup || [], steps: file.steps || [], watch: file.watch || [], judge: file.judge || [] };
    store.scenarioDraftDirty = false; store.scenarioDraftErrors = [];
    store.view = 'player'; store.scenarioScreen = 'editor';
}

export function closeScenarioEditor() {
    store.scenarioDraft = null; store.scenarioDraftDirty = false; store.scenarioDraftErrors = [];
    store.scenarioScreen = 'detail';
    // Re-enter a fresh Detail on the saved file (re-fetch), mirroring closeTopologyEditor's pattern.
    // If scenarioId is null (cancelling a brand-new scenario), leave it null so routing falls back to the list.
    if (store.scenarioId) openScenario(store.scenarioId);
}

export function validateScenarioDraft() {
    store.scenarioDraftErrors = scenarioErrors(store.scenarioDraft);
    return store.scenarioDraftErrors.length === 0;
}

// Resolve a "Block.Property" path from a scenario `set` row to { serviceId, propertyId } using store.config.
// Returns null when the block or property cannot be found (e.g. host not yet loaded or path typo).
function serviceIdForPath(path) {
    if (!store.config || !store.config.logicBlocks) return null;
    const dot = path.indexOf('.');
    if (dot < 0) return null;
    const blockName = path.slice(0, dot);
    const propertyId = path.slice(dot + 1);
    const lb = store.config.logicBlocks.find(b => b.name === blockName);
    if (!lb) return null;
    for (const service of (lb.services || [])) {
        const props = [
            ...(service.serviceProperties || []),
            ...(service.serviceMeasuringPoints || []),
        ];
        if (props.some(p => p.identifier.toLowerCase() === propertyId.toLowerCase())) {
            return { serviceId: service.id, propertyId };
        }
    }
    return null;
}

// Save the draft to disk via the existing validated PUT, then return to the read-only Detail of the saved
// file (re-fetch so the saved bytes are what's shown). Mirrors saveTopologyDraft.
export async function saveScenarioDraft() {
    const id = store.scenarioDraft && store.scenarioDraft.id;
    if (!id) { store.scenarioDraftErrors = ['id is required']; return false; }
    try {
        const res = await fetch(`/api/scenarios/${encodeURIComponent(id)}`, {
            method: 'PUT', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(store.scenarioDraft),
        });
        if (res.ok) {
            store.scenarioDraftDirty = false; store.scenarioDraftErrors = [];
            store.scenarioScreen = 'detail';
            await openScenario(id);     // re-enters the read-only Detail/run view on the saved file
            return true;
        }
        if (res.status === 403) { const b = await res.json().catch(() => ({})); showError(b.error || 'scenario saving is disabled'); return false; }
        const body = await res.json().catch(() => ({}));
        store.scenarioDraftErrors = body.errors || [`save failed (HTTP ${res.status})`];
        return false;
    } catch (err) { store.scenarioDraftErrors = [String(err.message ?? err)]; return false; }
}

// Q5: apply just the draft's `setup` against the running host so "use current value" reads a sane state,
// without a full run. Drives each setup `set` via the existing property-set path. (serviceProviderSet setups
// route through the HAL drive — reuse driveContract if present; otherwise note as a TODO for the rare case.)
export async function applySetup() {
    for (const s of (store.scenarioDraft && store.scenarioDraft.setup) || []) {
        if (s.set) {
            const resolved = serviceIdForPath(s.set);
            if (resolved) {
                await setProperty(resolved.serviceId, resolved.propertyId, s.value);
            } else {
                showError(`applySetup: could not resolve path '${s.set}' — block or property not found`);
            }
        }
        // serviceProviderSet rows are skipped here (they route through driveContract which requires
        // handlerName / spId / contractId context not present in the setup row): TODO when needed.
    }
}

// Read the live host value for a scenario name path — `Block.Property` or `Block.Property.Field`.
// For two-segment paths, resolves via serviceIdForPath → liveValue (the coalesced store.values entry).
// For a three-segment path (struct field navigation), navigates one level into the published object
// using a case-insensitive field lookup (matching the wire-key camelCase convention). Returns
// undefined when the path is unresolvable or the value has not yet been published.
export function currentValueFor(path) {
    if (!path || typeof path !== 'string') return undefined;
    const segments = path.split('.');
    // Base is always the first two segments (Block.Property); a third segment is a struct field name.
    const base = segments.slice(0, 2).join('.');
    const field = segments.length >= 3 ? segments[2] : null;
    const resolved = serviceIdForPath(base);
    if (!resolved) return undefined;
    const value = liveValue(resolved.serviceId, resolved.propertyId);
    if (field === null) return value;
    // Struct field navigation: case-tolerant (wire keys are camelCased, schema keys are usually
    // PascalCase — match case-insensitively, same policy as primeInitialValues).
    if (value === null || value === undefined || typeof value !== 'object') return undefined;
    if (Object.prototype.hasOwnProperty.call(value, field)) return value[field];
    const lower = field.toLowerCase();
    const key = Object.keys(value).find(k => k.toLowerCase() === lower);
    return key !== undefined ? value[key] : undefined;
}

async function refreshRun(id) {
    try {
        const response = await fetch(`/api/scenarios/${encodeURIComponent(id)}/run`);
        if (response.status === 404) return;
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const report = await response.json();
        if (store.scenarioId === id) {
            store.run = report;
            store.runActive = report.status === 'running';
        }
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

export async function applyScenario(id, { restart = false } = {}) {
    try {
        const query = restart ? '?restart=true' : '';
        const response = await fetch(`/api/scenarios/${encodeURIComponent(id)}/apply${query}`, { method: 'POST' });
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
        const body = await response.json().catch(() => ({}));
        if (body.recycling) {
            // Recycle-on-run: the host is rebuilding onto the scenario's topology with a clean slate (epoch
            // clock, fresh blocks). Park the run; the reconnect path (reinitClientState) re-applies it on the
            // fresh generation, where it runs in place. Drop the stale report and reflect the connection blip.
            store.pendingScenarioRun = { id, restart };
            store.run = null;
            store.connected = false;
            store.recycling = true;
            return;
        }

        store.runActive = true;
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
