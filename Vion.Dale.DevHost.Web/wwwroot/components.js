// All Explorer components. Plain-object Vue components with template strings (runtime-compiled —
// the no-build substrate, see CLAUDE.md). Conventions: components render from the store and
// format.js policy helpers; writable controls keep a local draft + dirty flag so incoming live
// updates never clobber an edit (the R0 guarantee, expressed the Vue way: the live value only
// flows into the control while it is not dirty).

import { computed, nextTick, onMounted, onUnmounted, ref, watch } from './vue.esm-browser.prod.js';
import {
    buildVerificationReport, contractTypeShort, cssGroupKey, defaultOpen, describeExpect, describeOutputAssert, describeType, describeWaitUntil,
    effectiveType, enumDisplay, enumMembers, formatTemporal, formatValue, gallerySamples,
    GROUP_LABELS, groupItems, isNullable, isWritable, matchesFilter, orderedGroupKeys, parseFilter,
    parseNamePath, presentationFacts, resolveAuthoredTitle, resolveDisplayName, resolveUnit, sampleJson, serviceMembers, severityFor,
    sampleX, shortTypeName, signTone, stepRibbonGeometry, traceLaneKind, traceNumericBand, traceSeriesFor, traceStateBands,
    STEP_GLYPHS,
} from './format.js';
import {
    applyScenario, baselineDelta, buildSharedContractLookup, changedCountForBlock,
    changedSinceBaseline, clearBaseline, clearPins, cloneTopologyDraft, closeScenario, collapseKey, connectionsForLb,
    advanceHost, driveContract, halKey, historyFor, isPinned, judgeKey, loadTopologies, newTopologyDraft, openScenario, pauseHost, resetHost, resumeHost, stepHost,
    saveTopologyDraft, setBaseline, setJudgeTick, setProperty, showError, store,
    switchClockMode, switchTopology, toggleCollapsed, togglePin, validateTopologyDraft, valueKey,
} from './store.js';
import { allowsMultiple, autoConnect, residueOf } from './wiring.js';

// Filter tokens, shared by every component that narrows to matches.
const filterTokens = computed(() => parseFilter(store.filter));

// Platform-aware modifier label: most users are on Windows/Linux (Ctrl), so don't show the mac ⌘ glyph
// to them. The Ctrl+K handler already accepts both ctrlKey and metaKey — this only affects the labels.
const IS_MAC = typeof navigator !== 'undefined' && /Mac|iPhone|iPad|iPod/.test(navigator.platform || navigator.userAgent || '');
const MOD_KEY = IS_MAC ? '⌘' : 'Ctrl';
const PALETTE_KEY_LABEL = IS_MAC ? '⌘K' : 'Ctrl K';

function itemMatches(service, item) {
    return matchesFilter(filterTokens.value, item, store.values[valueKey(service.id, item.identifier)]);
}

// ── shared helpers ──────────────────────────────────────────────────────────────

function useLive(props) {
    return computed(() => store.values[valueKey(props.service.id, props.item.identifier)]);
}

// 500 ms flash marker driven by live-value changes.
function useFlash(live) {
    const flashing = ref(false);
    let timer = null;
    watch(live, () => {
        flashing.value = true;
        clearTimeout(timer);
        timer = setTimeout(() => { flashing.value = false; }, 500);
    });
    return flashing;
}

// Badge descriptors for the docs expander (annotation chips demoted from the old always-on row).
function badgeList(item) {
    const isProperty = item._kind === 'property';
    const schema = item.schema || {};
    const presentation = item.presentation || {};
    const writeOnly = schema.writeOnly === true;
    const kind = schema['x-kind'];
    const uiHint = presentation.uiHint;
    const badges = [];
    const push = (cls, text, title) => badges.push({ cls, text, title: title || '' });

    push(isProperty ? 'writable' : 'readonly', isProperty ? 'property' : 'measuring point');
    if (enumMembers(schema)) push('enum', 'enum');
    if (isProperty) push(isWritable(item) ? 'writable' : 'readonly', isWritable(item) ? 'writable' : 'readonly');
    if (writeOnly) push('writeonly', 'writeOnly', 'runtime publishes a redacted sentinel');
    const importance = presentation.importance;
    if (importance) push(importance.toLowerCase(), importance.toLowerCase());
    if (kind) push(kind.toLowerCase(), kind, `MeasuringPointKind = ${kind}`);
    if (uiHint === 'statusIndicator' || presentation.statusMappings) push('statusindicator', 'status indicator');
    if (uiHint && uiHint !== 'statusIndicator') push('uihint', `uiHint=${uiHint}`);
    if (presentation.decimals !== undefined && presentation.decimals !== null) push('decimals', `${presentation.decimals} dp`);
    if (presentation.order !== undefined && presentation.order !== null) push('order', `order=${presentation.order}`);
    if (presentation.format) push('decimals', `format=${presentation.format}`);

    // RFC 0004: runtime emission policy (throttle/deadband/immediate) + persistence.
    const runtime = item.runtime || {};
    if (runtime.persistent) push('persistent', 'persistent', 'value survives a runtime restart');
    const t = runtime.throttle;
    if (t) {
        if (t.immediate) {
            push('throttle', 'immediate', 'RFC 0004: emitted on every change (throttle & deadband bypassed)');
        } else {
            const hasInterval = t.minInterval && t.minInterval !== '0' && t.minInterval !== '0ms';
            const bits = [];
            if (hasInterval) bits.push(t.minInterval);
            if (t.minChange) bits.push('Δ' + t.minChange);
            const label = hasInterval ? 'throttle' : 'deadband';
            push('throttle', bits.length ? `${label} ${bits.join(' · ')}` : 'throttle', 'RFC 0004 emission policy');
        }
    }
    return badges;
}

// ── value rendering ─────────────────────────────────────────────────────────────

// ── inline metric sparkline (hand-rolled SVG; no charting dependency, fully offline) ────────────

export const Sparkline = {
    props: ['values', 'width', 'height'],
    setup(props) {
        const w = computed(() => props.width || 64);
        const h = computed(() => props.height || 16);
        // A polyline over the finite numeric samples, normalised into the box. Hidden below two points
        // (a single sample is a dot, not a trend).
        const points = computed(() => {
            const vals = (props.values || []).filter(v => typeof v === 'number' && Number.isFinite(v));
            if (vals.length < 2) return null;
            const pad = 1.5;
            const min = Math.min(...vals);
            const max = Math.max(...vals);
            const range = max - min || 1;
            const innerW = w.value - 2 * pad;
            const innerH = h.value - 2 * pad;
            const stepX = innerW / (vals.length - 1);
            return vals
                .map((v, i) => `${(pad + i * stepX).toFixed(1)},${(pad + innerH * (1 - (v - min) / range)).toFixed(1)}`)
                .join(' ');
        });
        return { points, w, h };
    },
    template: `
        <svg v-if="points" class="sparkline" :width="w" :height="h" :viewBox="'0 0 ' + w + ' ' + h"
             preserveAspectRatio="none" aria-hidden="true">
            <polyline :points="points" fill="none"/>
        </svg>
    `,
};

// The live trend series for a numeric METRIC (a measuring point, or a property that is also one) — null
// otherwise (config properties, enums/status, structs, or a series too short to chart). Live-only.
function metricTrend(service, item) {
    if (!service || !item) return null;
    const presentation = item.presentation || {};
    const isStatus = presentation.uiHint === 'statusIndicator' || !!presentation.statusMappings;
    const t = effectiveType(item.schema || {});
    const isMetric = item._kind === 'measuringPoint' || item._alsoMetric === true;
    if (!isMetric || isStatus || (t !== 'number' && t !== 'integer')) return null;
    const series = historyFor(service.id, item.identifier);
    return series && series.length >= 2 ? series : null;
}

export const ValueCell = {
    components: { Sparkline },
    // `sample`: optional value override (the gallery's synthetic previews) — rendering policy is
    // identical to live values by construction because it IS the same code path.
    props: ['service', 'item', 'sample'],
    setup(props) {
        const live = props.sample === undefined ? useLive(props) : computed(() => props.sample);
        const flashing = useFlash(live);
        const schema = props.item.schema || {};
        const presentation = props.item.presentation || {};
        const writeOnly = schema.writeOnly === true;
        const isStatus = presentation.uiHint === 'statusIndicator' || !!presentation.statusMappings;
        const temporalFormat = schema.format === 'date-time' || schema.format === 'duration' ? schema.format : null;
        const unit = resolveUnit(schema);
        const itemType = effectiveType(schema);
        const enumLabels = presentation.enumLabels || null;

        // UiHints.Sparkline on a numeric array renders the value AS a sparkline (live value or gallery
        // sample) instead of a JSON chip; null (→ falls back to the chip) below two finite samples.
        const arraySeries = computed(() => {
            if (presentation.uiHint !== 'sparkline' || itemType !== 'array') return null;
            const v = live.value;
            if (!Array.isArray(v)) return null;
            const nums = v.filter(n => typeof n === 'number' && Number.isFinite(n));
            return nums.length >= 2 ? nums : null;
        });

        const display = computed(() => {
            const v = live.value;
            if (writeOnly) {
                if (v === '***') return '••••••• (set, hidden)';
                if (v === null) return '(cleared)';
                if (v === undefined) return '(not set)';
                return '⚠ ' + formatValue(v);
            }
            if (isStatus) {
                return v === null || v === undefined ? '—' : enumDisplay(enumLabels, String(v));
            }
            if (temporalFormat) {
                void store.relativeTick;
                return formatTemporal(v, temporalFormat, presentation.format || null);
            }
            if (v === undefined) return '-';
            if (enumLabels && typeof v === 'string') return enumDisplay(enumLabels, v);
            let text = formatValue(v, presentation.decimals ?? null);
            if (unit && typeof v === 'number' && (itemType === 'number' || itemType === 'integer')) {
                text += ` ${unit}`;
            }
            return text;
        });

        const severity = computed(() => isStatus ? severityFor(presentation.statusMappings, live.value) : null);
        return { display, severity, flashing, writeOnly, isStatus, arraySeries };
    },
    template: `
        <Sparkline v-if="arraySeries" :values="arraySeries" :width="84" :height="18"/>
        <span v-else-if="isStatus" class="severity-pill" :class="[severity, { updated: flashing }]">{{ display }}</span>
        <span v-else class="value-chip" :class="{ updated: flashing, secret: writeOnly }">{{ display }}</span>
    `,
};

// ── writable controls (draft + dirty pattern) ───────────────────────────────────

export const NumberControl = {
    props: ['service', 'item'],
    setup(props) {
        const live = useLive(props);
        const schema = props.item.schema || {};
        const nullable = isNullable(schema);
        const integer = effectiveType(schema) === 'integer';
        const text = ref(live.value === null || live.value === undefined ? '' : String(live.value));
        const dirty = ref(false);
        watch(live, v => { if (!dirty.value) text.value = v === null || v === undefined ? '' : String(v); });
        const commit = () => {
            dirty.value = false;
            const raw = text.value;
            const value = raw === '' ? null : (integer ? parseInt(raw, 10) : parseFloat(raw));
            setProperty(props.service.id, props.item.identifier, value);
        };
        const setNull = () => { dirty.value = false; setProperty(props.service.id, props.item.identifier, null); };
        const bounds = computed(() => schema.minimum !== undefined || schema.maximum !== undefined
            ? `${schema.minimum ?? '−∞'} – ${schema.maximum ?? '∞'}` : null);
        const unit = resolveUnit(schema);
        return { text, dirty, commit, setNull, nullable, schema, bounds, unit, integer };
    },
    template: `
        <span class="control">
            <input type="number" :step="integer ? '1' : 'any'" :min="schema.minimum" :max="schema.maximum"
                   :title="bounds" :value="text" :placeholder="nullable ? '∅' : ''"
                   @input="dirty = true; text = $event.target.value"
                   @blur="commit" @keydown.enter="commit(); $event.target.blur()">
            <span v-if="unit" class="unit">{{ unit }}</span>
            <button v-if="nullable" type="button" class="null-btn" title="set null" @click="setNull">×∅</button>
        </span>
    `,
};

export const TextControl = {
    props: ['service', 'item'],
    setup(props) {
        const live = useLive(props);
        const schema = props.item.schema || {};
        const nullable = isNullable(schema);
        const uiHint = (props.item.presentation || {}).uiHint;
        const multiline = uiHint === 'multiline' || uiHint === 'json';
        const text = ref(live.value === null || live.value === undefined ? '' : String(live.value));
        const dirty = ref(false);
        watch(live, v => { if (!dirty.value) text.value = v === null || v === undefined ? '' : String(v); });
        const commit = () => {
            dirty.value = false;
            setProperty(props.service.id, props.item.identifier, text.value === '' && nullable ? null : text.value);
        };
        const setNull = () => { dirty.value = false; setProperty(props.service.id, props.item.identifier, null); };
        return { text, dirty, commit, setNull, nullable, multiline, mono: uiHint === 'json' };
    },
    template: `
        <span class="control">
            <textarea v-if="multiline" rows="3" :spellcheck="!mono" :class="{ mono }" :value="text"
                      :placeholder="nullable ? '(empty = null)' : ''"
                      @input="dirty = true; text = $event.target.value" @blur="commit"></textarea>
            <input v-else type="text" :value="text" :placeholder="nullable ? '(empty = null)' : ''"
                   @input="dirty = true; text = $event.target.value"
                   @blur="commit" @keydown.enter="commit(); $event.target.blur()">
            <button v-if="nullable" type="button" class="null-btn" title="set null" @click="setNull">×∅</button>
        </span>
    `,
};

export const EnumSelect = {
    props: ['service', 'item'],
    setup(props) {
        const live = useLive(props);
        const schema = props.item.schema || {};
        const nullable = isNullable(schema);
        const members = enumMembers(schema) || [];
        const enumLabels = (props.item.presentation || {}).enumLabels || null;
        const current = computed(() => live.value === null || live.value === undefined ? '__NULL__' : String(live.value));
        const onChange = e => {
            const v = e.target.value;
            setProperty(props.service.id, props.item.identifier, v === '__NULL__' ? null : v);
        };
        const label = name => enumDisplay(enumLabels, name);
        return { members, nullable, current, onChange, label };
    },
    template: `
        <select class="control" :value="current" @change="onChange">
            <option v-if="nullable" value="__NULL__">— ∅ —</option>
            <option v-for="m in members" :key="m" :value="m">{{ label(m) }}</option>
        </select>
    `,
};

export const BoolToggle = {
    props: ['service', 'item'],
    setup(props) {
        const live = useLive(props);
        const checked = computed(() => !!live.value);
        const onChange = e => setProperty(props.service.id, props.item.identifier, e.target.checked);
        return { checked, onChange };
    },
    template: `<input class="toggle" type="checkbox" :checked="checked" @change="onChange">`,
};

export const TriggerButton = {
    props: ['service', 'item'],
    setup(props) {
        const run = () => setProperty(props.service.id, props.item.identifier, true);
        const name = resolveDisplayName(props.item);
        return { run, name };
    },
    template: `<button type="button" class="trigger-button" @click="run">Run {{ name }}</button>`,
};

export const SecretControl = {
    props: ['service', 'item'],
    setup(props) {
        const text = ref('');
        const set = () => { setProperty(props.service.id, props.item.identifier, text.value); text.value = ''; };
        const clear = () => setProperty(props.service.id, props.item.identifier, null);
        return { text, set, clear };
    },
    template: `
        <span class="control">
            <input type="password" placeholder="(new secret)" :value="text" @input="text = $event.target.value">
            <button type="button" @click="set">Set</button>
            <button type="button" @click="clear">Clear</button>
        </span>
    `,
};

// Struct / array editor with two tabs (mockup 03): a schema-generated FIELD FORM for flat
// structs (per-[StructField] units, bounds, descriptions; read-modify-write primed from the
// last published value) and the raw-JSON textarea (always available; the only mode for arrays
// and non-flat shapes). The dirty flag protects half-typed drafts from incoming live updates;
// a successful Set clears it. Structs are replaced as a whole on write.
export const JsonEditor = {
    props: ['service', 'item'],
    setup(props) {
        const live = useLive(props);
        const schema = props.item.schema || {};
        const nullable = isNullable(schema);

        // Flat-struct detection: an object whose every field is scalar / enum / string-format.
        const fieldEntries = computed(() => {
            if (effectiveType(schema) !== 'object' || !schema.properties) return null;
            const entries = Object.entries(schema.properties).map(([name, fieldSchema]) => {
                const t = effectiveType(fieldSchema);
                return {
                    name,
                    schema: fieldSchema,
                    type: t,
                    enums: enumMembers(fieldSchema),
                    nullable: isNullable(fieldSchema),
                    unit: resolveUnit(fieldSchema),
                    bounds: fieldSchema.minimum !== undefined || fieldSchema.maximum !== undefined
                        ? `${fieldSchema.minimum ?? '−∞'} – ${fieldSchema.maximum ?? '∞'}` : null,
                    description: fieldSchema.description || '',
                };
            });
            return entries.every(f => f.type !== 'object' && f.type !== 'array') ? entries : null;
        });
        const formSupported = computed(() => fieldEntries.value !== null);
        const tab = ref(formSupported.value ? 'form' : 'raw');

        // ── form state: name -> string draft (plus per-field null markers) ─────────
        const form = ref({});
        const nulls = ref({});
        const dirty = ref(false);
        const primeForm = v => {
            const next = {};
            const nextNulls = {};
            (fieldEntries.value || []).forEach(f => {
                const fv = v && typeof v === 'object' ? v[f.name] : undefined;
                nextNulls[f.name] = fv === null;
                next[f.name] = fv === null || fv === undefined ? '' : String(fv);
            });
            form.value = next;
            nulls.value = nextNulls;
        };
        const payload = () => {
            const out = {};
            (fieldEntries.value || []).forEach(f => {
                if (f.nullable && nulls.value[f.name]) {
                    out[f.name] = null;
                    return;
                }
                const raw = form.value[f.name];
                if (f.type === 'integer') out[f.name] = parseInt(raw, 10);
                else if (f.type === 'number') out[f.name] = parseFloat(raw);
                else if (f.type === 'boolean') out[f.name] = raw === 'true';
                else out[f.name] = raw;
            });
            return out;
        };
        const preview = computed(() => {
            void form.value;
            void nulls.value;
            try {
                return JSON.stringify(payload());
            } catch {
                return '';
            }
        });

        // ── raw state ───────────────────────────────────────────────────────────
        const text = ref('');
        const seed = v => v === null || v === undefined ? '' : JSON.stringify(v, null, 2);

        text.value = seed(live.value);
        if (formSupported.value) primeForm(live.value ?? sampleJson(schema));
        watch(live, v => {
            if (dirty.value) return;
            text.value = seed(v);
            if (formSupported.value) primeForm(v ?? sampleJson(schema));
        });

        const sample = JSON.stringify(sampleJson(schema), null, 2);
        const schemaJson = JSON.stringify(schema, null, 2);
        const fillTemplate = () => { text.value = sample; dirty.value = true; };
        const commitForm = () => {
            const value = payload();
            const bad = Object.entries(value).find(([, v]) => typeof v === 'number' && Number.isNaN(v));
            if (bad) {
                showError(`${props.item.identifier}.${bad[0]} is not a number`);
                return;
            }
            dirty.value = false;
            setProperty(props.service.id, props.item.identifier, value);
        };
        const commitRaw = () => {
            let parsed;
            try {
                parsed = JSON.parse(text.value);
            } catch (err) {
                showError(`Invalid JSON for ${props.item.identifier}: ${err.message}`);
                return;
            }
            dirty.value = false;
            setProperty(props.service.id, props.item.identifier, parsed);
        };
        const setNull = () => { dirty.value = false; setProperty(props.service.id, props.item.identifier, null); };
        const markDirty = () => { dirty.value = true; };
        const fieldEditable = f => !(f.nullable && nulls.value[f.name]);
        const rawDraftHint = computed(() => dirty.value && !formSupported.value);
        return {
            tab, formSupported, fieldEntries, form, nulls, dirty, preview, text, sample, schemaJson,
            fillTemplate, commitForm, commitRaw, setNull, markDirty, nullable, fieldEditable, rawDraftHint,
        };
    },
    template: `
        <div class="json-editor">
            <div v-if="formSupported" class="editor-tabs">
                <button type="button" :class="{ active: tab === 'form' }" @click="tab = 'form'">Form</button>
                <button type="button" :class="{ active: tab === 'raw' }" @click="tab = 'raw'">Raw JSON</button>
                <span v-if="dirty" class="draft-hint">draft — live updates held</span>
            </div>
            <template v-if="formSupported && tab === 'form'">
                <div v-for="f in fieldEntries" :key="f.name" class="field-row" :title="f.description">
                    <span class="mono field-name">{{ f.name }}</span>
                    <span v-if="f.bounds" class="field-bounds">{{ f.bounds }}</span>
                    <span class="item-spacer"></span>
                    <template v-if="fieldEditable(f)">
                        <select v-if="f.enums" :value="form[f.name]" @change="markDirty(); form[f.name] = $event.target.value">
                            <option v-for="m in f.enums" :key="m" :value="m">{{ m }}</option>
                        </select>
                        <select v-else-if="f.type === 'boolean'" :value="form[f.name]" @change="markDirty(); form[f.name] = $event.target.value">
                            <option value="true">true</option>
                            <option value="false">false</option>
                        </select>
                        <input v-else :type="f.type === 'integer' || f.type === 'number' ? 'number' : 'text'"
                               :step="f.type === 'integer' ? '1' : 'any'"
                               :min="f.schema.minimum" :max="f.schema.maximum" :value="form[f.name]"
                               @input="markDirty(); form[f.name] = $event.target.value">
                    </template>
                    <span v-if="f.unit" class="unit">{{ f.unit }}</span>
                    <label v-if="f.nullable" class="field-null">
                        <input type="checkbox" :checked="nulls[f.name]" @change="markDirty(); nulls[f.name] = $event.target.checked">∅
                    </label>
                </div>
                <div class="json-actions">
                    <span class="mono payload-preview" :title="preview">{{ preview }}</span>
                    <button type="button" @click="commitForm">Set value</button>
                    <button v-if="nullable" type="button" class="null-btn" @click="setNull">×∅</button>
                </div>
            </template>
            <template v-else>
                <details class="json-help"><summary>example &amp; schema</summary>
                    <div class="json-help-body">
                        <pre>{{ sample }}</pre>
                        <button type="button" title="Use as template" @click="fillTemplate">📋</button>
                    </div>
                    <details class="json-help-schema"><summary>full schema</summary><pre>{{ schemaJson }}</pre></details>
                </details>
                <textarea rows="4" spellcheck="false" class="mono" :value="text" placeholder="(paste / type JSON)"
                          @input="markDirty(); text = $event.target.value"></textarea>
                <div class="json-actions">
                    <button type="button" @click="commitRaw">Set JSON</button>
                    <button v-if="nullable" type="button" class="null-btn" @click="setNull">×∅</button>
                    <span v-if="rawDraftHint" class="draft-hint">draft — live updates held</span>
                </div>
            </template>
        </div>
    `,
};

// ── struct / array read-only viewer ─────────────────────────────────────────────
// The legible form of the value chip for big diagnostic data: a field grid for structs, a live
// table for arrays of flat structs (one column per [StructField], units in headers, enum labels
// in cells), a chip row for scalar/enum arrays. Pure rendering of the live value — updates as the
// block publishes.

export const StructViewer = {
    // `sample`: optional value override (the gallery's synthetic previews), same contract as
    // ValueCell — without it the viewer renders the live store value.
    props: ['service', 'item', 'sample'],
    setup(props) {
        const live = props.sample === undefined ? useLive(props) : computed(() => props.sample);
        const schema = props.item.schema || {};
        const t = effectiveType(schema);
        const enumLabels = (props.item.presentation || {}).enumLabels || null;

        const fieldDefs = objectSchema => Object.entries((objectSchema && objectSchema.properties) || {})
            .map(([name, fieldSchema]) => ({
                name,
                unit: resolveUnit(fieldSchema),
                description: fieldSchema.description || '',
                enums: enumMembers(fieldSchema) !== null,
            }));

        // mode: 'object' (field grid), 'table' (array of structs), 'list' (array of scalars)
        const elementSchema = t === 'array' ? schema.items || {} : null;
        const mode = t === 'object' ? 'object'
            : t === 'array' && effectiveType(elementSchema) === 'object' ? 'table' : 'list';
        const fields = mode === 'object' ? fieldDefs(schema) : mode === 'table' ? fieldDefs(elementSchema) : [];

        // Case-tolerant field access: wire keys are camelCased, schema property keys usually match,
        // but stay defensive (same policy as priming).
        const fieldValue = (row, name) => {
            if (row === null || row === undefined || typeof row !== 'object') return undefined;
            if (Object.prototype.hasOwnProperty.call(row, name)) return row[name];
            const lower = name.toLowerCase();
            const key = Object.keys(row).find(k => k.toLowerCase() === lower);
            return key !== undefined ? row[key] : undefined;
        };

        const fmtCell = (value, field) => {
            if (value === null || value === undefined) return '—';
            if (field && field.enums && typeof value === 'string') return enumDisplay(null, value);
            return formatValue(value);
        };

        const objectRows = computed(() => mode !== 'object' ? [] :
            fields.map(f => ({ ...f, value: fmtCell(fieldValue(live.value, f.name), f) })));
        const tableRows = computed(() => mode !== 'table' || !Array.isArray(live.value) ? [] : live.value);
        const listItems = computed(() => {
            if (mode !== 'list' || !Array.isArray(live.value)) return [];
            return live.value.map(v => v === null || v === undefined ? '—'
                : enumLabels && typeof v === 'string' ? enumDisplay(enumLabels, v) : formatValue(v));
        });

        const empty = computed(() => live.value === null || live.value === undefined
            || (Array.isArray(live.value) && live.value.length === 0));

        return { mode, fields, objectRows, tableRows, listItems, empty, fieldValue, fmtCell };
    },
    template: `
        <div class="struct-viewer">
            <div v-if="empty" class="viewer-empty">∅ — no value published</div>
            <template v-else-if="mode === 'object'">
                <div v-for="f in objectRows" :key="f.name" class="viewer-field" :title="f.description">
                    <span class="mono viewer-name">{{ f.name }}</span>
                    <span class="item-spacer"></span>
                    <span class="mono viewer-value">{{ f.value }}</span>
                    <span v-if="f.unit" class="unit">{{ f.unit }}</span>
                </div>
            </template>
            <div v-else-if="mode === 'table'" class="viewer-table-wrap">
                <table class="viewer-table">
                    <thead>
                        <tr>
                            <th class="viewer-idx">#</th>
                            <th v-for="f in fields" :key="f.name" :title="f.description">
                                {{ f.name }}<span v-if="f.unit" class="unit"> {{ f.unit }}</span>
                            </th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="(row, i) in tableRows" :key="i">
                            <td class="viewer-idx">{{ i }}</td>
                            <td v-for="f in fields" :key="f.name" class="mono">{{ fmtCell(fieldValue(row, f.name), f) }}</td>
                        </tr>
                    </tbody>
                </table>
            </div>
            <div v-else class="viewer-list">
                <span v-for="(v, i) in listItems" :key="i" class="viewer-chip mono">{{ v }}</span>
            </div>
        </div>
    `,
};

// ── docs expander (everything demoted from the old always-on table) ─────────────

export const DocsRow = {
    props: ['service', 'item'],
    setup(props) {
        const displayName = resolveDisplayName(props.item);
        const typeDisplay = describeType(props.item.schema);
        const description = props.item.schema && props.item.schema.description;
        const badges = badgeList(props.item);
        const docs = ['schema', 'presentation', 'runtime']
            .map(key => ({ key, json: JSON.stringify(props.item[key] ?? null, null, 2) }));
        return { displayName, typeDisplay, description, badges, docs };
    },
    template: `
        <div class="docs-row">
            <div class="docs-meta">
                <span class="docs-name">{{ displayName }}</span>
                <code class="docs-type">{{ typeDisplay }}</code>
                <span v-for="b in badges" class="badge" :class="b.cls" :title="b.title">{{ b.text }}</span>
            </div>
            <div v-if="description" class="docs-description">{{ description }}</div>
            <div class="docs-panels">
                <div v-for="d in docs" :key="d.key" class="docs-panel">
                    <h5>{{ d.key }}</h5>
                    <pre>{{ d.json }}</pre>
                </div>
            </div>
        </div>
    `,
};

// ── the dense row ───────────────────────────────────────────────────────────────

export const ItemRow = {
    components: { ValueCell, NumberControl, TextControl, EnumSelect, BoolToggle, TriggerButton, SecretControl, JsonEditor, DocsRow, StructViewer, Sparkline },
    props: ['lb', 'service', 'item'],
    setup(props) {
        const pinEntry = { block: props.lb.name, service: props.service.identifier, item: props.item.identifier };
        const pinned = computed(() => isPinned(pinEntry));
        const togglePinRow = () => togglePin(pinEntry);
        const changed = computed(() => changedSinceBaseline(valueKey(props.service.id, props.item.identifier)));
        const schema = props.item.schema || {};
        const presentation = props.item.presentation || {};
        const isProperty = props.item._kind === 'property';
        const writable = isProperty && isWritable(props.item);
        const writeOnly = schema.writeOnly === true;
        const t = effectiveType(schema);
        const isStruct = t === 'object' || t === 'array';
        const isStatus = presentation.uiHint === 'statusIndicator' || !!presentation.statusMappings;
        const hidden = presentation.importance === 'Hidden';
        const unit = resolveUnit(schema);

        // Control dispatch for writable scalars (value-as-control). Structs/arrays keep a value
        // chip plus an expandable editor row; writeOnly gets the secret control.
        const controlKind = computed(() => {
            if (!writable) return null;
            if (presentation.uiHint === 'trigger') return 'trigger';
            if (writeOnly) return 'secret';
            if (isStruct) return null;
            if (enumMembers(schema)) return 'enum';
            if (t === 'boolean') return 'bool';
            if (t === 'integer' || t === 'number') return 'number';
            return 'text';
        });

        const showStructEdit = computed(() => writable && isStruct);
        // Inline live trend for a numeric metric (its history ring buffer). Rendered just LEFT of the
        // value/control (adjacent to the input, after the spacer) so the value/input stays the rightmost
        // element and aligns — matters most for a property that is also a measuring point (input + trend).
        const trend = computed(() => metricTrend(props.service, props.item));
        const docsOpen = ref(false);
        const editorOpen = ref(false);
        const viewerOpen = ref(false);
        return { controlKind, docsOpen, editorOpen, viewerOpen, writable, isStruct, isStatus, hidden, unit, writeOnly, showStructEdit, trend, pinned, togglePinRow, changed };
    },
    template: `
        <div class="item" :class="{ 'hidden-importance': hidden }" :id="'item-' + service.id + '-' + item.identifier">
            <div class="item-row">
                <button type="button" class="docs-toggle" :class="{ open: docsOpen }" title="docs &amp; schema"
                        @click="docsOpen = !docsOpen">▸</button>
                <button type="button" class="pin-toggle" :class="{ pinned }"
                        :title="pinned ? 'unpin from watch' : 'pin to watch'" @click="togglePinRow">◆</button>
                <span class="item-name mono">{{ item.identifier }}</span>
                <code v-if="unit" class="unit-chip">{{ unit }}</code>
                <span v-if="changed" class="changed-dot" title="changed since baseline"></span>
                <span class="item-spacer"></span>
                <Sparkline v-if="trend" :values="trend" class="trend"/>
                <template v-if="controlKind">
                    <ValueCell v-if="isStatus" :service="service" :item="item"/>
                    <TriggerButton v-if="controlKind === 'trigger'" :service="service" :item="item"/>
                    <SecretControl v-else-if="controlKind === 'secret'" :service="service" :item="item"/>
                    <EnumSelect v-else-if="controlKind === 'enum'" :service="service" :item="item"/>
                    <BoolToggle v-else-if="controlKind === 'bool'" :service="service" :item="item"/>
                    <NumberControl v-else-if="controlKind === 'number'" :service="service" :item="item"/>
                    <TextControl v-else-if="controlKind === 'text'" :service="service" :item="item"/>
                </template>
                <template v-else>
                    <ValueCell :service="service" :item="item"/>
                    <button v-if="isStruct" type="button" class="edit-toggle"
                            :class="{ open: viewerOpen }" @click="viewerOpen = !viewerOpen">view</button>
                    <button v-if="showStructEdit" type="button" class="edit-toggle"
                            :class="{ open: editorOpen }" @click="editorOpen = !editorOpen">{ } edit</button>
                </template>
            </div>
            <StructViewer v-if="viewerOpen" :service="service" :item="item"/>
            <JsonEditor v-if="editorOpen" :service="service" :item="item"/>
            <DocsRow v-if="docsOpen" :service="service" :item="item"/>
        </div>
    `,
};

// ── groups / services / blocks ──────────────────────────────────────────────────

export const GroupSection = {
    components: { ItemRow },
    props: ['lb', 'service', 'groupKey', 'items'],
    setup(props) {
        const key = collapseKey(props.lb.name, props.service.identifier, props.groupKey);
        const filterActive = computed(() => filterTokens.value.length > 0);
        const visible = computed(() => filterActive.value
            ? props.items.filter(it => itemMatches(props.service, it))
            : props.items);
        // An active filter overrides collapse state: groups with matches force open, groups
        // without matches disappear entirely (the count in the topbar reports the hidden total).
        const collapsed = computed(() => {
            if (filterActive.value) return false;
            const explicit = store.collapsed[key];
            if (explicit !== undefined) return explicit;
            return !defaultOpen(props.groupKey, props.items.length);
        });
        const toggle = () => { if (!filterActive.value) toggleCollapsed(key, collapsed.value); };
        const label = GROUP_LABELS[props.groupKey] !== undefined ? GROUP_LABELS[props.groupKey] : props.groupKey;
        const css = cssGroupKey(props.groupKey);
        const countText = computed(() => filterActive.value ? `${visible.value.length} of ${props.items.length}` : `${props.items.length}`);
        return { collapsed, toggle, label, css, visible, filterActive, countText };
    },
    template: `
        <div v-if="visible.length" class="group-section" :class="css">
            <button type="button" class="group-header" @click="toggle">
                <span class="chevron" :class="{ open: !collapsed }">▸</span>
                <code class="group-key">{{ label }}</code>
                <span class="group-count">{{ countText }}</span>
            </button>
            <div v-if="!collapsed" class="group-items">
                <ItemRow v-for="it in visible" :key="it.identifier" :lb="lb" :service="service" :item="it"/>
            </div>
        </div>
    `,
};

export const PrimaryStrip = {
    components: { ValueCell },
    props: ['lb'],
    setup(props) {
        const entries = [];
        (props.lb.services || []).forEach(service => {
            serviceMembers(service).forEach(item => {
                if (item.presentation && item.presentation.importance === 'Primary') {
                    entries.push({ service, item });
                }
            });
        });
        return { entries };
    },
    template: `
        <div v-if="entries.length" class="primary-strip">
            <span class="strip-label">primary</span>
            <span v-for="e in entries" :key="e.service.id + e.item.identifier" class="primary-tile">
                <span class="mono">{{ e.item.identifier }}</span>
                <ValueCell :service="e.service" :item="e.item"/>
            </span>
        </div>
    `,
};

export const WiringSection = {
    props: ['lb', 'sharedLookup'],
    setup(props) {
        const connections = connectionsForLb(props.lb.id);
        const contractInfoMap = {};
        (props.lb.contracts || []).forEach(c => { contractInfoMap[c.identifier] = c; });
        const contracts = (props.lb.contractMappings || []).map(cm => {
            const info = contractInfoMap[cm.contractIdentifier];
            if (!info) return null;
            const spId = cm.mappedServiceProviderIdentifier;
            const svcId = cm.mappedServiceIdentifier;
            const cId = cm.mappedContractIdentifier;
            const endpointKey = `${spId}/${svcId}/${cId}`;
            const sharedWith = (props.sharedLookup[endpointKey] || []).filter(x => x.lbId !== props.lb.id).map(x => x.lbName);
            const type = info.matchingContractType;
            return {
                id: cm.contractIdentifier, type,
                short: contractTypeShort(type),
                handler: info.annotations && info.annotations.contractHandlerActorName,
                spId, svcId, cId, sharedWith,
            };
        }).filter(Boolean);

        // The live value of any contract, keyed by (sp, svc, contract) — fed by the one generic
        // ServiceProviderContractChanged event. The control type (toggle / field / read-out) comes from c.short.
        const halValue = (c) => store.hal[halKey(c.spId, c.svcId, c.cId)];
        const onDi = (c, e) => driveContract(c.handler, c.spId, c.svcId, c.cId, e.target.checked);
        const onAi = (c, e) => driveContract(c.handler, c.spId, c.svcId, c.cId, parseFloat(e.target.value) || 0);
        return { connections, contracts, halValue, onDi, onAi, fmt: formatValue };
    },
    template: `
        <details v-if="connections.length || contracts.length" class="wiring">
            <summary>wiring <span class="group-count">{{ connections.length }} links · {{ contracts.length }} contracts</span></summary>
            <div class="connection-badges">
                <span v-for="c in connections" class="connection-badge">
                    <b>{{ c.arrow }}</b> {{ c.otherName }} <i>via {{ c.sourceIface }} ↔ {{ c.targetIface }}</i>
                </span>
            </div>
            <div v-for="c in contracts" :key="c.spId + c.svcId + c.cId" class="io-row">
                <span class="contract-type-badge" :class="c.short.toLowerCase()" :title="c.type">{{ c.short }}</span>
                <span class="mono io-name">{{ c.id }}</span>
                <span v-if="c.sharedWith.length" class="shared-badge">shared with {{ c.sharedWith.join(', ') }}</span>
                <span class="item-spacer"></span>
                <input v-if="c.short === 'DI'" class="toggle" type="checkbox" :checked="!!halValue(c)" @change="onDi(c, $event)">
                <input v-else-if="c.short === 'AI'" type="number" step="0.1" :value="halValue(c) ?? 0"
                       @keydown.enter="onAi(c, $event); $event.target.blur()" @blur="onAi(c, $event)">
                <span v-else-if="c.short === 'DO' || c.short === 'AO'" class="value-chip">{{ fmt(halValue(c) ?? (c.short === 'DO' ? false : 0)) }}</span>
                <span v-else class="scenario-only" :title="c.type + ' — drive/assert from a scenario (serviceProviderSet / serviceProviderExpect)'">scenario-driven</span>
            </div>
        </details>
    `,
};

export const BlockCard = {
    components: { PrimaryStrip, WiringSection, GroupSection },
    props: ['lb', 'sharedLookup'],
    setup(props) {
        const services = (props.lb.services || []).map(service => {
            const itemsByGroup = groupItems(service);
            const blockGroups = props.lb.annotations && Array.isArray(props.lb.annotations.Groups) ? props.lb.annotations.Groups : [];
            const groups = orderedGroupKeys(blockGroups, itemsByGroup).map(key => ({ key, items: itemsByGroup[key] }));
            return { service, groups };
        });
        const totals = computed(() => {
            let writable = 0, total = 0;
            (props.lb.services || []).forEach(s => {
                serviceMembers(s).forEach(m => { total++; if (m._kind === 'property' && isWritable(m)) writable++; });
            });
            return { writable, total };
        });
        const icon = props.lb.annotations && props.lb.annotations.Icon;
        const multiService = (props.lb.services || []).length > 1;
        // With an active filter, a block with zero matches disappears (the rail still lists it).
        const visibleCard = computed(() => {
            if (!filterTokens.value.length) return true;
            return (props.lb.services || []).some(service =>
                [...(service.serviceProperties || []), ...(service.serviceMeasuringPoints || [])]
                    .some(item => itemMatches(service, item)));
        });
        return { services, totals, icon, multiService, visibleCard };
    },
    template: `
        <section v-if="visibleCard" class="block-card" :id="'block-' + lb.id">
            <div class="block-header">
                <h2>{{ lb.name }}</h2>
                <code v-if="icon" class="icon-chip" title="Remixicon name">{{ icon }}</code>
                <span class="item-spacer"></span>
                <span class="block-counts">{{ totals.writable }} writable · {{ totals.total - totals.writable }} readonly</span>
            </div>
            <PrimaryStrip :lb="lb"/>
            <WiringSection :lb="lb" :sharedLookup="sharedLookup"/>
            <div v-for="s in services" :key="s.service.id" class="service-section">
                <h3 v-if="multiService">service: {{ s.service.identifier }}</h3>
                <GroupSection v-for="g in s.groups" :key="g.key" :lb="lb" :service="s.service" :groupKey="g.key" :items="g.items"/>
            </div>
        </section>
    `,
};

export const Rail = {
    props: [],
    setup() {
        const blocks = computed(() => (store.config && store.config.logicBlocks) || []);
        const count = lb => (lb.services || []).reduce((n, s) => n + serviceMembers(s).length, 0);
        const select = lb => {
            store.selectedBlockId = lb.id;
            const el = document.getElementById('block-' + lb.id);
            if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
        };
        const changedCount = lb => changedCountForBlock(lb);
        return { blocks, count, select, store, changedCount };
    },
    template: `
        <nav class="rail">
            <button v-for="lb in blocks" :key="lb.id" type="button" class="rail-item"
                    :class="{ active: store.selectedBlockId === lb.id }" @click="select(lb)">
                <span class="rail-dot"></span>
                <span class="rail-name">{{ lb.name }}</span>
                <span v-if="store.baseline && changedCount(lb)" class="rail-changed"
                      title="changed since baseline">{{ changedCount(lb) }}</span>
                <span v-else class="rail-count">{{ count(lb) }}</span>
            </button>
        </nav>
    `,
};

// ── watch panel (pinned tiles, drive above observe) ─────────────────────────────

const WatchTile = {
    components: { ValueCell, NumberControl, TextControl, EnumSelect, BoolToggle, TriggerButton, SecretControl, StructViewer, Sparkline },
    props: ['entry'],
    setup(props) {
        // Resolve the name-path pin against the current topology; null → tombstone.
        const resolved = computed(() => {
            const lb = (store.config && store.config.logicBlocks || []).find(b => b.name === props.entry.block);
            const service = lb && (lb.services || []).find(s => s.identifier === props.entry.service);
            if (!service) return null;
            const prop = (service.serviceProperties || []).find(p => p.identifier === props.entry.item);
            const mp = (service.serviceMeasuringPoints || []).find(p => p.identifier === props.entry.item);
            if (!prop && !mp) return null;
            // Flag a member that is both so its trend sparkline shows even when pinned via the property side.
            return { lb, service, item: { ...(prop || mp), _kind: prop ? 'property' : 'measuringPoint', _alsoMetric: !!(prop && mp) } };
        });
        const trend = computed(() => resolved.value ? metricTrend(resolved.value.service, resolved.value.item) : null);
        const controlKind = computed(() => {
            const r = resolved.value;
            if (!r) return null;
            const schema = r.item.schema || {};
            const presentation = r.item.presentation || {};
            if (r.item._kind !== 'property' || !isWritable(r.item)) return null;
            if (presentation.uiHint === 'trigger') return 'trigger';
            if (schema.writeOnly === true) return 'secret';
            const t = effectiveType(schema);
            if (t === 'object' || t === 'array') return null;
            if (enumMembers(schema)) return 'enum';
            if (t === 'boolean') return 'bool';
            if (t === 'integer' || t === 'number') return 'number';
            return 'text';
        });
        const changed = computed(() => {
            const r = resolved.value;
            return r ? changedSinceBaseline(valueKey(r.service.id, r.item.identifier)) : false;
        });
        const delta = computed(() => {
            const r = resolved.value;
            if (!r) return null;
            const d = baselineDelta(valueKey(r.service.id, r.item.identifier));
            if (d === null || d === 0) return null;
            const decimals = (r.item.presentation || {}).decimals ?? null;
            return `${d > 0 ? '+' : ''}${formatValue(d, decimals)} since baseline`;
        });
        const unpin = () => togglePin(props.entry);
        const isStruct = computed(() => {
            const r = resolved.value;
            if (!r) return false;
            const t = effectiveType(r.item.schema || {});
            return t === 'object' || t === 'array';
        });
        const viewerOpen = ref(false);
        const showTrend = computed(() => !!trend.value && !viewerOpen.value);
        return { resolved, controlKind, changed, delta, unpin, isStruct, viewerOpen, trend, showTrend };
    },
    template: `
        <div class="watch-tile">
            <template v-if="resolved">
                <div class="tile-head">
                    <span class="mono tile-name">{{ entry.item }}</span>
                    <span v-if="changed" class="changed-dot" title="changed since baseline"></span>
                    <button v-if="isStruct" type="button" class="tile-view" :class="{ open: viewerOpen }"
                            :title="viewerOpen ? 'collapse' : 'expand'" @click="viewerOpen = !viewerOpen">▸</button>
                    <button type="button" class="tile-unpin" title="unpin" @click="unpin">✕</button>
                </div>
                <div class="tile-block">{{ entry.block }}</div>
                <StructViewer v-if="viewerOpen" :service="resolved.service" :item="resolved.item"/>
                <div v-if="!viewerOpen" class="tile-body">
                    <TriggerButton v-if="controlKind === 'trigger'" :service="resolved.service" :item="resolved.item"/>
                    <SecretControl v-else-if="controlKind === 'secret'" :service="resolved.service" :item="resolved.item"/>
                    <EnumSelect v-else-if="controlKind === 'enum'" :service="resolved.service" :item="resolved.item"/>
                    <BoolToggle v-else-if="controlKind === 'bool'" :service="resolved.service" :item="resolved.item"/>
                    <NumberControl v-else-if="controlKind === 'number'" :service="resolved.service" :item="resolved.item"/>
                    <TextControl v-else-if="controlKind === 'text'" :service="resolved.service" :item="resolved.item"/>
                    <ValueCell v-else :service="resolved.service" :item="resolved.item"/>
                </div>
                <Sparkline v-if="showTrend" :values="trend" :width="160" :height="26" class="trend tile-trend"/>
                <div v-if="delta" class="tile-delta">{{ delta }}</div>
            </template>
            <template v-else>
                <div class="tile-head">
                    <span class="mono tile-name tombstone" :title="'not present in this topology: ' + entry.block + '/' + entry.service + '/' + entry.item">{{ entry.item }}</span>
                    <button type="button" class="tile-unpin" title="unpin" @click="unpin">✕</button>
                </div>
                <div class="tile-block">{{ entry.block }} — not in this topology</div>
            </template>
        </div>
    `,
};

export const WatchPanel = {
    components: { WatchTile },
    setup() {
        // A pin resolves when its name path still exists in the CURRENT topology — switches and
        // renames turn pins into tombstones, and a switch can orphan many at once.
        const resolvePin = entry => {
            const lb = ((store.config && store.config.logicBlocks) || []).find(b => b.name === entry.block);
            const service = lb && (lb.services || []).find(s => s.identifier === entry.service);
            if (!service) return null;
            return [...(service.serviceProperties || []), ...(service.serviceMeasuringPoints || [])]
                .find(p => p.identifier === entry.item) || null;
        };
        const drive = computed(() => store.pins.filter(p => {
            const item = resolvePin(p);
            return item && isWritable(item);
        }));
        const observe = computed(() => store.pins.filter(p => !drive.value.includes(p)));
        const missing = computed(() => store.pins.filter(p => !resolvePin(p)));
        const empty = computed(() => store.pins.length === 0);
        const clearAll = () => clearPins();
        const pruneMissing = () => clearPins(missing.value);
        return { store, drive, observe, missing, empty, clearAll, pruneMissing };
    },
    template: `
        <aside class="watch" :class="{ empty }">
            <template v-if="empty">
                <span class="watch-hint">pin to watch · ◆</span>
            </template>
            <template v-else>
                <div class="watch-header">
                    <span>watch · {{ store.pins.length }}</span>
                    <span class="item-spacer"></span>
                    <button type="button" class="watch-clear" title="unpin everything (c)" @click="clearAll">✕ clear</button>
                </div>
                <button v-if="missing.length" type="button" class="watch-prune"
                        title="unpin everything the current topology does not resolve"
                        @click="pruneMissing">remove {{ missing.length }} not in this topology</button>
                <div v-if="drive.length" class="watch-section">drive</div>
                <WatchTile v-for="p in drive" :key="p.block + '/' + p.service + '/' + p.item" :entry="p"/>
                <div v-if="observe.length" class="watch-section">observe</div>
                <WatchTile v-for="p in observe" :key="p.block + '/' + p.service + '/' + p.item" :entry="p"/>
            </template>
        </aside>
    `,
};

// ── topology editor (RFC 0013): the edit sub-mode of the topology panel ─────────────────────────
// Mutates store.topologyDraft (the draft + dirty discipline). Phase 2 covers the block list and the
// catalog picker; wiring / validate / save land in later tasks.

// Add a logic-block instance from the definition catalog: pick a type, name it, add. Names must be
// non-empty, unique within the draft, and dotless (the server rejects dots in instance names) — an
// invalid name shows an inline hint and is not added.
const BlockPicker = {
    setup() {
        const selectedType = ref('');
        const name = ref('');
        const definitions = computed(() => store.definitions || []);
        const options = computed(() => definitions.value.map(d => ({
            typeFullName: d.typeFullName,
            short: shortTypeName(d.typeFullName),
        })));
        const instances = computed(() => (store.topologyDraft && store.topologyDraft.logicBlockInstances) || []);
        const trimmedName = computed(() => name.value.trim());
        const nameHasDot = computed(() => trimmedName.value.indexOf('.') >= 0);
        const nameTaken = computed(() => instances.value.some(b => b.name === trimmedName.value));
        // The inline hint explains why "add" is blocked; empty name is the resting state (no hint, just
        // a disabled button) so the picker doesn't nag before the user has typed anything.
        const hint = computed(() => {
            if (!trimmedName.value) return '';
            if (nameHasDot.value) return 'name cannot contain a dot';
            if (nameTaken.value) return 'name already used in this draft';
            return '';
        });
        const canAdd = computed(() => {
            if (!store.topologyDraft) return false;
            if (!selectedType.value) return false;
            if (!trimmedName.value) return false;
            return !nameHasDot.value && !nameTaken.value;
        });
        const add = () => {
            if (!canAdd.value) return;
            store.topologyDraft.logicBlockInstances.push({ typeFullName: selectedType.value, name: trimmedName.value });
            store.topologyDraftDirty = true;
            name.value = '';
        };
        return { selectedType, name, options, hint, canAdd, add };
    },
    template: `
        <div class="topo-row topo-picker">
            <select v-model="selectedType">
                <option value="" disabled>add block…</option>
                <option v-for="o in options" :key="o.typeFullName" :value="o.typeFullName">{{ o.short }}</option>
            </select>
            <input type="text" placeholder="name" v-model="name" @keyup.enter="add"/>
            <button type="button" class="theme-toggle" :disabled="!canAdd" @click="add">+ add</button>
            <span v-if="hint" class="topo-hint">{{ hint }}</span>
        </div>
    `,
};

// One existing interface mapping rendered as `src.iface → tgt.iface` with an unwire button.
const WiringRow = {
    props: ['mapping', 'index'],
    setup(props, { emit }) {
        const remove = () => emit('unwire', props.index);
        return { remove };
    },
    template: `
        <div class="topo-row">
            <span class="mono topo-name">{{ mapping.sourceLogicBlockName }}.{{ mapping.sourceInterfaceIdentifier }}</span>
            <span class="topo-arrow">→</span>
            <span class="mono topo-name">{{ mapping.targetLogicBlockName }}.{{ mapping.targetInterfaceIdentifier }}</span>
            <span class="item-spacer"></span>
            <button type="button" class="theme-toggle" title="remove this wire" @click="remove">✕</button>
        </div>
    `,
};

// One residue entry: a required/contested unwired interface, its multiplicity hint, and a candidate
// picker. The select handler lives here (not the template) so no logic leaks into the markup.
const ResidueRow = {
    props: ['entry'],
    setup(props, { emit }) {
        // kind 'required' -> a warning pill; 'contested' -> a neutral "pick one".
        const isRequired = computed(() => props.entry.kind === 'required');
        const pillClass = computed(() => isRequired.value ? 'severity-pill warning' : 'severity-pill neutral');
        const pillLabel = computed(() => isRequired.value ? 'needs wiring' : 'pick one');
        const multHint = computed(() => allowsMultiple(props.entry.multiplicity) ? 'fan-in' : 'single-writer');
        // The select stays unselected (placeholder option) — picking a candidate fires the wire and the
        // residue recomputes (the entry usually disappears), so there is nothing to keep selected.
        const onPick = event => {
            const idx = event.target.selectedIndex - 1; // option 0 is the placeholder
            event.target.selectedIndex = 0;
            const cand = (props.entry.candidates || [])[idx];
            if (cand) emit('wire', props.entry.blockName, props.entry.interfaceIdentifier, cand.targetName, cand.targetInterface);
        };
        return { pillClass, pillLabel, multHint, onPick };
    },
    template: `
        <div class="topo-row topo-residue-row">
            <span class="mono topo-name">{{ entry.blockName }}.{{ entry.interfaceIdentifier }}</span>
            <span :class="pillClass">{{ pillLabel }}</span>
            <span class="topo-meta">{{ multHint }}</span>
            <span class="item-spacer"></span>
            <select v-if="entry.candidates.length" @change="onPick">
                <option value="">wire to…</option>
                <option v-for="(c, ci) in entry.candidates" :key="ci" :value="ci">{{ c.targetName }}.{{ c.targetInterface }}</option>
            </select>
            <span v-else class="topo-meta">no candidate</span>
        </div>
    `,
};

const TopologyEditor = {
    components: { BlockPicker, WiringRow, ResidueRow },
    setup(props, { emit }) {
        const draft = computed(() => store.topologyDraft);
        const instances = computed(() => (draft.value && draft.value.logicBlockInstances) || []);
        const mappings = computed(() => (draft.value && draft.value.interfaceMappings) || []);
        const onIdInput = () => { store.topologyDraftDirty = true; };
        const close = () => emit('done');
        // Removing a block also drops any interface mappings that reference its name on either end — a
        // dangling wire would fail validation, so keep the draft internally consistent as blocks go.
        const removeBlock = index => {
            const d = draft.value;
            if (!d) return;
            const removed = d.logicBlockInstances[index];
            if (!removed) return;
            d.logicBlockInstances.splice(index, 1);
            d.interfaceMappings = (d.interfaceMappings || []).filter(m => m.sourceLogicBlockName !== removed.name && m.targetLogicBlockName !== removed.name);
            store.topologyDraftDirty = true;
        };
        const shortFor = typeFullName => shortTypeName(typeFullName);

        // ── wiring (Task 6) ────────────────────────────────────────────────────────
        // residueOf is pure (wiring.js) over the live draft — the un/under-wired required interfaces and
        // the contested single-writer ones the author still has to resolve.
        const residue = computed(() => residueOf(store.definitions, (draft.value && draft.value.logicBlockInstances) || [], (draft.value && draft.value.interfaceMappings) || []));
        const wire = (srcName, srcIface, tgtName, tgtIface) => {
            if (!draft.value) return;
            draft.value.interfaceMappings.push({ sourceLogicBlockName: srcName, sourceInterfaceIdentifier: srcIface, targetLogicBlockName: tgtName, targetInterfaceIdentifier: tgtIface });
            store.topologyDraftDirty = true;
        };
        const unwire = index => {
            if (!draft.value) return;
            draft.value.interfaceMappings.splice(index, 1);
            store.topologyDraftDirty = true;
        };
        // Wire every unambiguous pair and leave contested ones for the author; idempotent — autoConnect only
        // adds missing pairs, so a second click "wires the rest" once the author has resolved some by hand.
        const runAutoConnect = () => {
            if (!draft.value) return;
            draft.value.interfaceMappings = autoConnect(store.definitions, draft.value.logicBlockInstances, draft.value.interfaceMappings);
            store.topologyDraftDirty = true;
        };

        // ── form ⇄ raw + validate/save/switch (Task 7) ─────────────────────────────
        const tab = ref('form');
        // Raw-tab textarea: a local draft seeded from store.topologyDraft on entry (the draft+dirty
        // discipline). The store draft is only mutated on an explicit commit, not on every keystroke.
        const rawText = ref('');
        const seedRaw = () => { try { rawText.value = JSON.stringify(draft.value, null, 2); } catch { rawText.value = ''; } };
        const showRaw = () => { seedRaw(); tab.value = 'raw'; };
        const showForm = () => { tab.value = 'form'; };
        const commitRaw = () => {
            try {
                store.topologyDraft = JSON.parse(rawText.value);
                store.topologyDraftDirty = true;
                store.topologyDraftErrors = [];
            } catch (e) {
                store.topologyDraftErrors = ['invalid JSON: ' + e.message];
            }
        };

        // Validate flips a one-shot "did a validate just run" flag so a clean pass can show a green pill
        // (errors-empty alone is the resting state, not a success signal).
        const validated = ref(false);
        const errors = computed(() => store.topologyDraftErrors || []);
        const hasErrors = computed(() => errors.value.length > 0);
        const showValid = computed(() => validated.value && errors.value.length === 0);
        const validate = async () => { await validateTopologyDraft(); validated.value = true; };
        const dirty = computed(() => store.topologyDraftDirty);
        const save = async () => { validated.value = false; await saveTopologyDraft(); };
        const saveAndSwitch = async () => {
            validated.value = false;
            const ok = await saveTopologyDraft();
            if (ok) {
                switchTopology(draft.value.id);
                emit('done');
            }
        };
        // Any draft mutation invalidates a prior validate verdict.
        watch([instances, mappings, () => draft.value && draft.value.id], () => { validated.value = false; }, { deep: true });

        return {
            draft, instances, mappings, onIdInput, close, removeBlock, shortFor,
            residue, wire, unwire, runAutoConnect,
            tab, rawText, showRaw, showForm, commitRaw,
            errors, hasErrors, showValid, validate, dirty, save, saveAndSwitch,
        };
    },
    template: `
        <div class="topo-panel" v-if="draft">
            <div class="topo-row topo-editor-head">
                <div class="editor-tabs">
                    <button type="button" :class="{ active: tab === 'form' }" @click="showForm">form</button>
                    <button type="button" :class="{ active: tab === 'raw' }" @click="showRaw">{ } raw</button>
                </div>
                <span class="item-spacer"></span>
                <button type="button" class="theme-toggle" title="close the editor" @click="close">✕</button>
            </div>

            <template v-if="tab === 'form'">
                <div class="topo-row topo-editor-head">
                    <span class="topo-meta">id</span>
                    <input type="text" class="topo-id-input" placeholder="topology id" v-model="draft.id" @input="onIdInput"/>
                </div>
                <h3 class="topo-section">blocks</h3>
                <div v-if="!instances.length" class="topo-meta">no blocks yet — add one below</div>
                <div v-for="(b, i) in instances" :key="b.name" class="topo-row">
                    <span class="mono topo-name">{{ b.name }}</span>
                    <span class="item-spacer"></span>
                    <span class="topo-meta mono">{{ shortFor(b.typeFullName) }}</span>
                    <button type="button" class="theme-toggle" title="remove this block" @click="removeBlock(i)">✕</button>
                </div>
                <BlockPicker/>

                <div class="topo-section-head">
                    <h3 class="topo-section">wiring</h3>
                    <span class="item-spacer"></span>
                    <button type="button" class="theme-toggle"
                            title="wire every unambiguous pair; contested ones are left for you. Click again to wire the rest."
                            @click="runAutoConnect">⚡ AutoConnect</button>
                </div>
                <div v-if="!mappings.length" class="topo-meta">no wires yet — AutoConnect, or pick from residue below</div>
                <WiringRow v-for="(m, i) in mappings" :key="i" :mapping="m" :index="i" @unwire="unwire"/>

                <template v-if="residue.length">
                    <h3 class="topo-section">residue</h3>
                    <ResidueRow v-for="(e, i) in residue" :key="i" :entry="e" @wire="wire"/>
                </template>

                <div class="topo-row topo-footer">
                    <button type="button" class="theme-toggle" @click="validate">validate</button>
                    <span v-if="showValid" class="severity-pill success">valid</span>
                    <span class="item-spacer"></span>
                    <button type="button" class="theme-toggle" :disabled="!dirty" title="save this topology file" @click="save">save</button>
                    <button type="button" class="theme-toggle" title="save, then recycle the host onto this topology" @click="saveAndSwitch">save &amp; switch</button>
                </div>
                <div v-if="hasErrors" class="topo-errors">
                    <div v-for="(err, i) in errors" :key="i" class="topo-row topo-error-row">
                        <span class="severity-pill error">error</span>
                        <span class="topo-meta">{{ err }}</span>
                    </div>
                </div>
            </template>

            <template v-else>
                <textarea rows="18" spellcheck="false" class="mono topo-raw" :value="rawText"
                          placeholder="(topology JSON)" @input="rawText = $event.target.value"></textarea>
                <div class="topo-row topo-footer">
                    <button type="button" class="theme-toggle" title="parse and replace the draft" @click="commitRaw">commit JSON</button>
                </div>
                <div v-if="hasErrors" class="topo-errors">
                    <div v-for="(err, i) in errors" :key="i" class="topo-row topo-error-row">
                        <span class="severity-pill error">error</span>
                        <span class="topo-meta">{{ err }}</span>
                    </div>
                </div>
            </template>
        </div>
    `,
};

// ── topology panel: the read-only setup view — what runs, how it is wired, where IO lands ──────

export const TopologyPanel = {
    components: { TopologyEditor },
    setup() {
        const blocks = computed(() => (store.config && store.config.logicBlocks) || []);
        const links = computed(() => (store.config && store.config.interfaceMappings) || []);
        const providers = computed(() => (store.config && store.config.serviceProviders) || []);

        // view ⇄ edit sub-mode (RFC 0013): the read-only setup view, or the topology editor on a draft.
        // Editing is gated to writable topology workspaces (a read-only host can still observe).
        const mode = ref('view');
        const canEdit = computed(() => !(store.topologies && store.topologies.readOnly));
        const startNew = () => { newTopologyDraft(); mode.value = 'edit'; };
        const startClone = id => { cloneTopologyDraft(id); mode.value = 'edit'; };
        // An external requester (the ⌘K palette / keybinding) opens the editor by bumping
        // store.topologyEditRequest after setting the topology view; the panel reacts by flipping into
        // edit mode. The requester has already primed the draft (new/clone). Not immediate — the counter
        // starts at 0 and must only act on a genuine change.
        watch(() => store.topologyEditRequest, () => { mode.value = 'edit'; });

        // Topology files (R5): refreshed on every panel open; switching rides the reset recovery.
        loadTopologies();
        const topologyFiles = computed(() => (store.topologies && store.topologies.topologies) || []);
        const canSwitch = computed(() => !!(store.topologies && store.topologies.canSwitch));
        // The live config's name leads — the discovery payload can be a generation behind right
        // after a switch, before reinit re-fetches it.
        const currentTopology = computed(() => store.topologyName || (store.topologies && store.topologies.current));
        const doSwitch = id => switchTopology(id);
        const counts = lb => {
            let writable = 0, total = 0;
            (lb.services || []).forEach(s => {
                serviceMembers(s).forEach(m => { total++; if (m._kind === 'property' && isWritable(m)) writable++; });
            });
            return `${total} properties · ${writable} writable`;
        };
        const contractRows = lb => {
            const infoMap = {};
            (lb.contracts || []).forEach(c => { infoMap[c.identifier] = c; });
            return (lb.contractMappings || []).map(cm => {
                const info = infoMap[cm.contractIdentifier];
                const type = info ? info.matchingContractType : '?';
                return {
                    id: cm.contractIdentifier, type,
                    short: contractTypeShort(type),
                    endpoint: `${cm.mappedServiceProviderIdentifier} / ${cm.mappedServiceIdentifier} / ${cm.mappedContractIdentifier}`,
                };
            });
        };
        return { store, blocks, links, providers, counts, contractRows, topologyFiles, canSwitch, currentTopology, doSwitch, mode, canEdit, startNew, startClone };
    },
    template: `
        <div class="topology-panel">
            <template v-if="mode === 'view'">
            <section class="block-card">
                <div class="block-header">
                    <h2>topology</h2>
                    <code v-if="store.topologyName" class="topology-chip">{{ store.topologyName }}</code>
                    <span class="item-spacer"></span>
                    <span class="block-counts">{{ blocks.length }} blocks · {{ links.length }} links · {{ providers.length }} mock providers</span>
                </div>
                <h3 class="topo-section">blocks</h3>
                <div v-for="lb in blocks" :key="lb.id" class="topo-row">
                    <span class="mono topo-name">{{ lb.name }}</span>
                    <code v-if="lb.annotations?.Icon" class="icon-chip">{{ lb.annotations.Icon }}</code>
                    <span class="item-spacer"></span>
                    <span class="topo-meta">{{ counts(lb) }}</span>
                </div>
                <h3 class="topo-section">links</h3>
                <div v-for="(m, i) in links" :key="i" class="topo-row">
                    <span class="mono topo-name">{{ m.sourceLogicBlockName }}</span>
                    <span class="topo-arrow">→</span>
                    <span class="mono topo-name">{{ m.targetLogicBlockName }}</span>
                    <span class="item-spacer"></span>
                    <span class="topo-meta mono">{{ m.sourceInterfaceIdentifier }} ↔ {{ m.targetInterfaceIdentifier }}</span>
                </div>
                <div v-if="!links.length" class="topo-meta">no inter-block links</div>
                <h3 class="topo-section">hardware contracts (mocked)</h3>
                <template v-for="lb in blocks" :key="'c' + lb.id">
                    <div v-for="c in contractRows(lb)" :key="lb.id + c.id" class="topo-row">
                        <span class="contract-type-badge" :class="c.short.toLowerCase()">{{ c.short }}</span>
                        <span class="mono topo-name">{{ lb.name }}.{{ c.id }}</span>
                        <span class="item-spacer"></span>
                        <span class="topo-meta mono">{{ c.endpoint }}</span>
                    </div>
                </template>
                <div class="topo-section-head">
                    <h3 class="topo-section">topology files</h3>
                    <span class="item-spacer"></span>
                    <button v-if="canEdit" type="button" class="theme-toggle" title="author a new topology"
                            @click="startNew">+ new</button>
                </div>
                <div v-if="!topologyFiles.length" class="topo-meta">
                    no topology files — export this preset with <code>dale dev --export-topology topologies/&lt;id&gt;.topology.json</code>
                </div>
                <div v-for="t in topologyFiles" :key="t.id" class="topo-row">
                    <span class="mono topo-name">{{ t.id }}</span>
                    <span v-if="t.error" class="scenario-error" :title="t.error">invalid</span>
                    <span v-else class="topo-meta">{{ t.blocks }} blocks</span>
                    <span class="item-spacer"></span>
                    <button v-if="canEdit" type="button" class="theme-toggle" title="clone this topology into the editor"
                            @click="startClone(t.id)">✎ clone</button>
                    <code v-if="t.id === currentTopology" class="topology-chip">running</code>
                    <button v-else type="button" class="theme-toggle" :disabled="!canSwitch"
                            :title="canSwitch ? 'recycle the host into this topology' : 'switching needs a topology-aware supervisor (DevHostWebRunner.RunAsync with a Func<string?, IDevHost> factory)'"
                            @click="doSwitch(t.id)">⇄ switch</button>
                </div>
            </section>
            </template>
            <template v-else>
                <TopologyEditor @done="mode = 'view'"/>
            </template>
        </div>
    `,
};

// ── presentation gallery: how authored metadata renders, on synthetic sample values ────────────
// The attribute-payoff lever (R2.5): per block, every property rendered through the SAME display
// components the explorer uses (ValueCell / StructViewer with a sample override), with values
// derived purely from introspection metadata — bounds become min·mid·max, every enum member and
// status mapping appears, temporals show authored formats, nullable shows the ∅ case. Authored
// presentation chips and "not authored" hints make the payoff (and the gaps) explicit.

const GalleryItem = {
    components: { ValueCell, StructViewer },
    props: ['lb', 'service', 'item'],
    setup(props) {
        const schema = props.item.schema || {};
        const t = effectiveType(schema);
        const isStruct = t === 'object' || t === 'array';
        const samples = gallerySamples(props.item);
        const facts = presentationFacts(props.item);
        const displayName = resolveAuthoredTitle(props.item);
        const typeDisplay = describeType(schema);
        return { samples, facts, isStruct, displayName, typeDisplay };
    },
    template: `
        <div class="gallery-item">
            <div class="gallery-head">
                <span class="item-name mono">{{ item.identifier }}</span>
                <span v-if="displayName" class="gallery-display">{{ displayName }}</span>
                <code class="docs-type">{{ typeDisplay }}</code>
                <span class="item-spacer"></span>
                <span v-for="f in facts.authored" class="fact-chip">{{ f }}</span>
            </div>
            <div v-if="facts.missing.length" class="gallery-missing">not authored: {{ facts.missing.join(' · ') }}</div>
            <div class="gallery-samples">
                <template v-if="isStruct">
                    <StructViewer :service="service" :item="item" :sample="samples[0].value"/>
                </template>
                <template v-else>
                    <span v-for="(s, i) in samples" :key="i" class="gallery-sample">
                        <span v-if="s.label" class="sample-label">{{ s.label }}</span>
                        <ValueCell :service="service" :item="item" :sample="s.value"/>
                    </span>
                </template>
            </div>
        </div>
    `,
};

export const GalleryCard = {
    components: { GalleryItem },
    props: ['lb'],
    setup(props) {
        const services = (props.lb.services || []).map(service => {
            const itemsByGroup = groupItems(service);
            const blockGroups = props.lb.annotations && Array.isArray(props.lb.annotations.Groups) ? props.lb.annotations.Groups : [];
            const groups = orderedGroupKeys(blockGroups, itemsByGroup).map(key => ({
                key,
                label: GROUP_LABELS[key] !== undefined ? GROUP_LABELS[key] : key,
                css: cssGroupKey(key),
                items: itemsByGroup[key],
            }));
            return { service, groups };
        });
        // The block-level payoff metric: how many items still have high-value authoring gaps
        // (the per-shape `missing` policy) — "0 gaps" is the target, not "something authored".
        let gaps = 0, total = 0;
        services.forEach(s => s.groups.forEach(g => g.items.forEach(item => {
            total++;
            if (presentationFacts(item).missing.length) gaps++;
        })));
        const gapSummary = gaps === 0
            ? `all ${total} fully authored`
            : `authoring gaps on ${gaps} of ${total}`;
        // An active filter narrows the same way the explorer does: matchless groups disappear,
        // and the whole card hides when nothing in it matches.
        const view = computed(() => services.map(s => ({
            service: s.service,
            groups: s.groups
                .map(g => filterTokens.value.length ? { ...g, items: g.items.filter(it => itemMatches(s.service, it)) } : g)
                .filter(g => g.items.length),
        })));
        const icon = props.lb.annotations && props.lb.annotations.Icon;
        const multiService = (props.lb.services || []).length > 1;
        const visibleCard = computed(() => view.value.some(s => s.groups.length));
        return { view, gapSummary, icon, multiService, visibleCard };
    },
    template: `
        <section v-if="visibleCard" class="block-card gallery-card" :id="'block-' + lb.id">
            <div class="block-header">
                <h2>{{ lb.name }}</h2>
                <code v-if="icon" class="icon-chip" title="Remixicon name">{{ icon }}</code>
                <span class="item-spacer"></span>
                <span class="block-counts">{{ gapSummary }}</span>
            </div>
            <div v-for="s in view" :key="s.service.id" class="service-section">
                <h3 v-if="multiService">service: {{ s.service.identifier }}</h3>
                <div v-for="g in s.groups" :key="g.key" class="group-section" :class="g.css">
                    <div class="group-header gallery-group-header">
                        <code class="group-key">{{ g.label }}</code>
                        <span class="group-count">{{ g.items.length }}</span>
                    </div>
                    <GalleryItem v-for="it in g.items" :key="it.identifier" :lb="lb" :service="s.service" :item="it"/>
                </div>
            </div>
        </section>
    `,
};

// ── Player (RFC 0006): scenario list, working-set view, run progress, judgments, report ────────
// The Player renders ONLY a scenario's working set: ordered steps with server-side run state
// (polled — F5-safe, agent-visible), the watch tiles, and the human-judgment checklist. The web UI
// triggers and renders runs; it never executes them — the C# ScenarioRunner is the one evaluator.

// Resolve a scenario name path against the live config — the same two/three-segment semantics the
// server validator applies (two-segment requires a unique carrier among the block's services).
function resolveNamePath(path) {
    const parsed = parseNamePath(path);
    if (!parsed) return null;
    const lb = ((store.config && store.config.logicBlocks) || []).find(b => b.name === parsed.block);
    if (!lb) return null;
    const carries = svc => [...(svc.serviceProperties || []), ...(svc.serviceMeasuringPoints || [])]
        .some(p => p.identifier === parsed.property);
    let service = null;
    if (parsed.service) {
        service = (lb.services || []).find(s => s.identifier === parsed.service);
        if (service && !carries(service)) service = null;
    } else {
        const carriers = (lb.services || []).filter(carries);
        if (carriers.length > 1) {
            // Same rule as the server resolver: never silent last-wins — tell the author how to qualify.
            return { ambiguous: carriers.map(s => `${parsed.block}.${s.identifier}.${parsed.property}`) };
        }
        service = carriers.length === 1 ? carriers[0] : null;
    }
    if (!service) return null;
    const prop = (service.serviceProperties || []).find(p => p.identifier === parsed.property);
    const mp = !prop && (service.serviceMeasuringPoints || []).find(p => p.identifier === parsed.property);
    return { lb, service, item: { ...(prop || mp), _kind: prop ? 'property' : 'measuringPoint' } };
}

const ScenarioWatchTile = {
    components: { ValueCell, StructViewer },
    props: ['path'],
    setup(props) {
        const resolved = computed(() => {
            const r = resolveNamePath(props.path);
            return r && r.item ? r : null;
        });
        const ambiguity = computed(() => {
            const r = resolveNamePath(props.path);
            return r && r.ambiguous ? `ambiguous — qualify: ${r.ambiguous.join(' or ')}` : 'does not resolve in this topology';
        });
        const isStruct = computed(() => {
            const r = resolved.value;
            if (!r) return false;
            const t = effectiveType(r.item.schema || {});
            return t === 'object' || t === 'array';
        });
        const viewerOpen = ref(false);
        return { resolved, ambiguity, isStruct, viewerOpen };
    },
    template: `
        <div class="watch-tile">
            <template v-if="resolved">
                <div class="tile-head">
                    <span class="mono tile-name">{{ path }}</span>
                    <button v-if="isStruct" type="button" class="tile-view" :class="{ open: viewerOpen }"
                            :title="viewerOpen ? 'collapse' : 'expand'" @click="viewerOpen = !viewerOpen">▸</button>
                </div>
                <StructViewer v-if="viewerOpen" :service="resolved.service" :item="resolved.item"/>
                <div v-if="!viewerOpen" class="tile-body">
                    <ValueCell :service="resolved.service" :item="resolved.item"/>
                </div>
            </template>
            <template v-else>
                <div class="tile-head">
                    <span class="mono tile-name tombstone">{{ path }}</span>
                </div>
                <div class="tile-block">{{ ambiguity }}</div>
            </template>
        </div>
    `,
};

const PlayerStep = {
    props: ['step'],
    setup(props) {
        const glyph = computed(() => STEP_GLYPHS[props.step.status] || '◌');
        const elapsed = computed(() => {
            const ms = props.step.elapsedMs;
            if (ms === null || ms === undefined) return '';
            return ms >= 1000 ? `${(ms / 1000).toFixed(1)} s` : `${Math.round(ms)} ms`;
        });
        // Big struct/array payloads don't fit the one-line chip — click toggles a pretty-printed
        // expansion (non-JSON arguments like waitUntil conditions just unwrap).
        const expanded = ref(false);
        const argText = computed(() => {
            if (!expanded.value) return props.step.argument;
            try {
                return JSON.stringify(JSON.parse(props.step.argument), null, 2);
            } catch {
                return props.step.argument;
            }
        });
        const toggleArg = () => { expanded.value = !expanded.value; };
        // A failed step is a MECHANICAL failure the runner detected (mockup 05) — distinct from a human
        // "judged not ok" verdict. Point at the file to fix; scenario edits hot-reload, so the loop is
        // edit JSON → re-run, no restart.
        const remediation = computed(() => store.scenarioId
            ? `scenarios/${store.scenarioId}.scenario.json · edits reload automatically`
            : 'edits reload automatically');
        return { glyph, elapsed, expanded, argText, toggleArg, remediation };
    },
    template: `
        <div class="player-step" :class="step.status">
            <span class="step-glyph">{{ glyph }}</span>
            <code class="step-kind">{{ step.kind }}</code>
            <span class="mono step-target">{{ step.target }}</span>
            <code v-if="step.argument" class="step-arg" :class="{ expanded }"
                  :title="expanded ? 'collapse' : 'expand'" @click="toggleArg">{{ argText }}</code>
            <span v-if="step.label" class="step-label">{{ step.label }}</span>
            <code v-if="step.spec" class="spec-chip">{{ step.spec }}</code>
            <span class="item-spacer"></span>
            <span v-if="step.detail" class="step-detail" :title="step.detail">{{ step.detail }}</span>
            <span v-if="elapsed" class="step-elapsed">{{ elapsed }}</span>
            <div v-if="step.status === 'failed'" class="step-remediation">↳ {{ remediation }}</div>
        </div>
    `,
};

// ── Scenario trace viewer (RFC 0012 §5, form C) ─────────────────────────────────
// One shared virtual-time axis: a step ribbon (segments ∝ duration, colored by status, captioned by
// label) over time-aligned signal lanes, with a draggable playhead. Geometry is pure (format.js); the
// components below are thin SVG/template projections. Coordinate space is 0..1 in x; the view width is
// fixed by CSS and the viewBox is 0..1000 so 1 unit = 0.1%.

const RIBBON_W = 1000;

const TraceStepRibbon = {
    props: ['geometry', 'activeIndex'],
    emits: ['select'],
    setup(props, { emit }) {
        const segs = computed(() => (props.geometry.steps || []).map(s => ({
            ...s,
            x: s.x0 * RIBBON_W,
            w: Math.max(2, (s.x1 - s.x0) * RIBBON_W),
        })));
        const pick = i => emit('select', i);
        return { segs, pick, RIBBON_W };
    },
    template: `
        <svg class="trace-ribbon" :viewBox="'0 0 ' + RIBBON_W + ' 26'" preserveAspectRatio="none" width="100%" height="26">
            <g v-for="s in segs" :key="s.index">
                <rect class="ribbon-seg" :class="[s.status, { active: s.index === activeIndex }]"
                      :x="s.x + 1" y="2" :width="Math.max(1, s.w - 2)" height="22" rx="2"
                      @click="pick(s.index)"><title>{{ s.kind }} · {{ s.label }}</title></rect>
            </g>
        </svg>
    `,
};

const TraceLaneNumeric = {
    props: ['series', 'geometry', 'playhead'],
    setup(props) {
        const band = computed(() => traceNumericBand(props.series));
        // Honest stairstep: hold each sample's value until the next sample's x, then step. y is inverted
        // (0 = top). null/undefined samples break the line (no segment drawn across a gap).
        const yOf = v => {
            const { min, max } = band.value;
            const t = (v - min) / (max - min || 1);
            const pad = 6;
            return pad + (1 - t) * (100 - 2 * pad);
        };
        const path = computed(() => {
            const pts = (props.series || []).map(s => ({ x: sampleX(props.geometry, s.stepIndex) * 1000, v: s.value }));
            let d = '';
            let prev = null;
            pts.forEach((p, i) => {
                const has = typeof p.v === 'number' && Number.isFinite(p.v);
                if (!has) { prev = null; return; }
                const y = yOf(p.v);
                if (prev === null) { d += ` M ${p.x.toFixed(1)} ${y.toFixed(1)}`; }
                else { d += ` H ${p.x.toFixed(1)} V ${y.toFixed(1)}`; }
                prev = p;
            });
            return d.trim() || null;
        });
        const zeroY = computed(() => yOf(0));
        return { band, path, zeroY };
    },
    template: `
        <svg class="trace-lane numeric" viewBox="0 0 1000 100" preserveAspectRatio="none" width="100%" height="40">
            <line class="lane-zero" x1="0" :y1="zeroY" x2="1000" :y2="zeroY"/>
            <path v-if="path" class="lane-line" :d="path" fill="none" vector-effect="non-scaling-stroke"/>
            <line v-if="playhead != null" class="lane-playhead" :x1="playhead * 1000" :x2="playhead * 1000" y1="0" y2="100"/>
        </svg>
    `,
};

const TraceLaneState = {
    props: ['series', 'geometry', 'playhead'],
    setup(props) {
        // Distinct values get distinct tones cycling a small token-driven palette; booleans read as
        // on/off. Each band is one segment; the label shows when the segment is wide enough.
        const bands = computed(() => {
            const raw = traceStateBands(props.series, props.geometry);
            const distinct = [...new Set(raw.map(b => String(b.value)))];
            return raw.map(b => ({
                x: b.x0 * 1000,
                w: Math.max(0, (b.x1 - b.x0) * 1000),
                label: b.value === null || b.value === undefined ? '∅' : String(b.value),
                tone: 'tone-' + (distinct.indexOf(String(b.value)) % 4),
            }));
        });
        return { bands };
    },
    template: `
        <svg class="trace-lane state" viewBox="0 0 1000 24" preserveAspectRatio="none" width="100%" height="24">
            <g v-for="(b, i) in bands" :key="i">
                <rect class="state-band" :class="b.tone" :x="b.x" y="2" :width="Math.max(1, b.w)" height="20" rx="2"/>
                <text v-if="b.w > 70" class="state-label" :x="b.x + 6" y="16">{{ b.label }}</text>
            </g>
            <line v-if="playhead != null" class="lane-playhead" :x1="playhead * 1000" :x2="playhead * 1000" y1="0" y2="24"/>
        </svg>
    `,
};

const ScenarioTrace = {
    components: { TraceStepRibbon, TraceLaneNumeric, TraceLaneState },
    // props: run = the report (with steps + watchTrace), paths = scenario.watch (the declared order).
    props: ['run', 'paths'],
    setup(props) {
        const geometry = computed(() => stepRibbonGeometry((props.run && props.run.steps) || [], { minFrac: 0.05 }));
        // The scrubber index walks the watchTrace samples (0 = start, then one per step reached).
        const samples = computed(() => (props.run && props.run.watchTrace) || []);
        const scrub = ref(0);
        // Snap to the end-state only when a NEW run lands (runId changes) — NOT on every idle re-poll of
        // the same run, so a scrubbed position holds while the Player keeps polling the report.
        watch(() => props.run && props.run.runId, () => { scrub.value = Math.max(0, samples.value.length - 1); }, { immediate: true });
        const maxScrub = computed(() => Math.max(0, samples.value.length - 1));
        const activeStepIndex = computed(() => {
            const s = samples.value[scrub.value];
            return s ? s.stepIndex : -1;
        });

        // One row per declared watch path — STATIC per run (series + lane kind + unit): independent of the
        // scrub position, so dragging the playhead does NOT recompute series or re-resolve schemas. This
        // keeps scrubbing O(rows) cheap even for many watch items, long traces, or large topologies.
        const rows = computed(() => (props.paths || []).map(path => {
            const series = traceSeriesFor(samples.value, path);
            const resolved = resolveNamePath(path);
            const schema = resolved && resolved.item ? (resolved.item.schema || null) : null;
            const firstNonNull = (series.find(s => s.value !== null && s.value !== undefined) || {}).value;
            return { path, series, kind: traceLaneKind(schema, firstNonNull), unit: schema ? resolveUnit(schema) : null };
        }));
        // The only scrub-dependent read: the value of a row at the scrubbed sample (cheap O(1) lookup).
        const currentOf = row => (row.series[Math.min(scrub.value, row.series.length - 1)] || {}).value;

        const selectStep = stepIndex => {
            const i = samples.value.findIndex(s => s.stepIndex === stepIndex);
            if (i >= 0) scrub.value = i;
        };
        const readout = row => {
            const v = currentOf(row);
            if (v === null || v === undefined) return '∅';
            if (typeof v === 'number') return formatValue(v) + (row.unit ? ' ' + row.unit : '');
            if (typeof v === 'object') return '{ … }';
            return String(v);
        };
        // Struct/array lanes show the scrubbed sample's value (JSON), NOT the block's live value — so a
        // struct lane snapshots to the playhead like every other lane (RFC 0012 §5).
        const structJson = row => formatValue(currentOf(row));
        const tone = row => { const v = currentOf(row); return row.kind === 'numeric' && v !== null && v !== undefined ? 'tone-' + signTone(v) : ''; };
        // The playhead x (0..1) of the scrubbed sample — a vertical cursor drawn across every lane.
        const playheadX = computed(() => sampleX(geometry.value, activeStepIndex.value));
        // The virtual time at the scrubbed sample (deterministic stepping). Null on a real clock — there we
        // fall back to the phase / step label so the readout is never blank.
        const scrubTime = computed(() => {
            const s = samples.value[scrub.value];
            if (!s) return '';
            if (s.virtualElapsedMs === null || s.virtualElapsedMs === undefined) {
                return s.phase === 'start' || s.stepIndex < 0 ? 'start' : `step ${s.stepIndex + 1}`;
            }
            const ms = s.virtualElapsedMs;
            return 't+' + (ms >= 1000 ? `${(ms / 1000).toFixed(ms % 1000 === 0 ? 0 : 1)} s` : `${Math.round(ms)} ms`);
        });
        return { geometry, samples, scrub, maxScrub, activeStepIndex, rows, selectStep, readout, structJson, tone, playheadX, scrubTime };
    },
    template: `
        <div class="scenario-trace">
            <div class="trace-row trace-axis">
                <span></span>
                <TraceStepRibbon :geometry="geometry" :active-index="activeStepIndex" @select="selectStep"/>
            </div>
            <div class="trace-row trace-axis">
                <span class="trace-time mono" :title="'virtual time at the playhead'">{{ scrubTime }}</span>
                <input class="trace-scrubber" type="range" min="0" :max="maxScrub" step="1" v-model.number="scrub"
                       :title="'sample ' + scrub + ' of ' + maxScrub + (scrubTime ? ' · ' + scrubTime : '')"/>
            </div>
            <div v-for="row in rows" :key="row.path" class="trace-row">
                <div class="trace-row-label">
                    <span class="trace-name mono" :title="row.path">{{ row.path }}</span>
                    <span class="trace-readout mono" :class="tone(row)" :title="readout(row)">{{ readout(row) }}</span>
                </div>
                <TraceLaneNumeric v-if="row.kind === 'numeric'" :series="row.series" :geometry="geometry" :playhead="playheadX"/>
                <TraceLaneState v-else-if="row.kind === 'state'" :series="row.series" :geometry="geometry" :playhead="playheadX"/>
                <div v-else class="trace-struct mono">{{ structJson(row) }}</div>
            </div>
        </div>
    `,
};

export const PlayerPanel = {
    components: { PlayerStep, ScenarioWatchTile, ScenarioTrace },
    setup() {
        const entries = computed(() => (store.scenarios && store.scenarios.scenarios) || []);
        const directory = computed(() => (store.scenarios && store.scenarios.directory) || '');
        const scenario = computed(() => store.scenario);
        const run = computed(() => store.run && store.run.scenarioId === store.scenarioId ? store.run : null);
        const running = computed(() => !!run.value && run.value.status === 'running');

        // Topology note: when the open scenario targets a different topology than the live host, running
        // recycles the host onto it (recycle-on-run). On a supervised host that just works; otherwise the
        // run is refused and the user must switch the host's topology first.
        const mismatch = computed(() => {
            if (!scenario.value) return false;
            return scenario.value.topology !== store.topologyName;
        });
        const mismatchText = computed(() => {
            if (!scenario.value) return '';
            const host = store.topologyName ? `'${store.topologyName}'` : 'no declared topology';
            return store.canReset
                ? `Running recycles the host onto topology '${scenario.value.topology}' (currently ${host}) for a clean, reproducible run.`
                : `This scenario expects topology '${scenario.value.topology}' — the host runs ${host} and has no supervisor to recycle. Switch the host's topology first.`;
        });

        // Before the first run: pending-shaped rows from the file, so the working set is visible
        // immediately. After: the server report is the truth. Defensive against structurally invalid
        // files — the list keeps them clickable on purpose (the error panel explains them).
        const fileSteps = section => {
            const raw = scenario.value && Array.isArray(scenario.value[section]) ? scenario.value[section] : [];
            return raw.map((s, i) => ({
                index: i,
                kind: s.set !== undefined ? 'set'
                    : s.serviceProviderSet ? 'serviceProviderSet'
                    : s.serviceProviderExpect ? 'serviceProviderExpect'
                    : s.waitUntil ? 'waitUntil'
                    : s.expect ? 'expect'
                    : s.advance ? 'advance'
                    : s.settle !== undefined ? 'settle' : 'unknown',
                label: s.label,
                spec: s.spec,
                target: s.set !== undefined ? s.set
                    : s.serviceProviderSet ? `${s.serviceProviderSet.logicBlock}.${s.serviceProviderSet.contract}`
                    : s.serviceProviderExpect ? `${s.serviceProviderExpect.logicBlock}.${s.serviceProviderExpect.contract}`
                    : s.waitUntil ? s.waitUntil.property
                    : s.expect ? s.expect.property
                    : s.advance ? ''
                    : s.settle !== undefined ? (s.settle.until ? `until ${s.settle.until.join(', ')}` : 'until stable') : '?',
                argument: s.serviceProviderExpect ? describeOutputAssert(s.serviceProviderExpect)
                    : 'value' in s ? JSON.stringify(s.value)
                    : s.waitUntil ? describeWaitUntil(s.waitUntil, s.timeoutSeconds)
                    : s.expect ? describeExpect(s.expect)
                    : s.advance ? `${s.advance.seconds} s`
                    : s.settle !== undefined ? (s.settle.maxSeconds !== undefined ? `≤${s.settle.maxSeconds} s` : '≤60 s')
                    : null,
                status: 'pending',
            }));
        };
        const setupSteps = computed(() => run.value ? run.value.setup : fileSteps('setup'));
        const steps = computed(() => run.value ? run.value.steps : fileSteps('steps'));
        const judge = computed(() => run.value ? run.value.judge
            : ((scenario.value && scenario.value.judge) || []).map(j => ({ text: j.text, spec: j.spec, status: 'requiresHuman' })));

        const statusClass = computed(() => run.value ? run.value.status : 'none');
        // The displayed run report belongs to a specific file version; after an edit + reload the
        // hashes diverge and the report is evidence about an older file.
        const staleRun = computed(() => {
            const r = run.value;
            if (!r) return false;
            if (!r.fileHash) return false;
            if (!store.scenarioFileHash) return false;
            return r.fileHash !== store.scenarioFileHash;
        });
        const runLabel = computed(() => running.value ? '⟳ restart' : run.value ? '↻ run again' : '▶ run');
        const heading = computed(() => (scenario.value && scenario.value.title) || store.scenarioId);
        // The structural parse error from discovery, when this file is broken — kept clickable on
        // purpose; the Player explains instead of crashing on a half-shaped working set.
        const entryError = computed(() => {
            const entries2 = (store.scenarios && store.scenarios.scenarios) || [];
            const entry = entries2.find(e => e.id === store.scenarioId);
            return entry ? entry.error : null;
        });
        const start = () => applyScenario(store.scenarioId, { restart: running.value });
        const tick = (index, verdict) => {
            if (run.value) setJudgeTick(run.value.runId, index, verdict);
        };
        const tickState = index => run.value ? store.judgeTicks[judgeKey(run.value.runId, index)] || null : null;
        const copyReport = async () => {
            try {
                await navigator.clipboard.writeText(buildVerificationReport(scenario.value, run.value, store.judgeTicks));
            } catch (err) {
                showError(`Could not copy the report: ${err.message ?? err}`);
            }
        };
        const reload = () => openScenario(store.scenarioId);

        // Tag filter: narrow the scenario list to a free-form spec tag (the scenario-level `specs`). The
        // chip row hides when nothing is tagged; clicking the active tag clears it.
        const tagFilter = ref(null);
        const allTags = computed(() => {
            const set = new Set();
            for (const e of entries.value) for (const t of (e.specs || [])) set.add(t);
            return Array.from(set).sort();
        });
        const filteredEntries = computed(() =>
            tagFilter.value ? entries.value.filter(e => (e.specs || []).includes(tagFilter.value)) : entries.value);
        const toggleTag = t => { tagFilter.value = tagFilter.value === t ? null : t; };

        return {
            store, entries, directory, scenario, run, running, mismatch, mismatchText, setupSteps,
            steps, judge, statusClass, staleRun, runLabel, heading, entryError, start, tick,
            tickState, copyReport, reload, open: openScenario, close: closeScenario,
            tagFilter, allTags, filteredEntries, toggleTag,
        };
    },
    template: `
        <div class="player-panel">
            <section v-if="!store.scenarioId" class="block-card">
                <div class="block-header">
                    <h2>scenarios</h2>
                    <span class="item-spacer"></span>
                    <span class="block-counts">{{ entries.length }} discovered · {{ directory }}</span>
                </div>
                <div v-if="allTags.length" class="scenario-tags">
                    <span class="scenario-tags-label">tag</span>
                    <button v-for="t in allTags" :key="t" type="button" class="scenario-tag"
                            :class="{ 'tag-active': tagFilter === t }"
                            :title="tagFilter === t ? 'clear filter' : 'show only scenarios tagged ' + t"
                            @click="toggleTag(t)">{{ t }}</button>
                </div>
                <div v-if="!entries.length" class="player-empty">
                    No scenario files. Create <code>scenarios/&lt;id&gt;.scenario.json</code> (schema:
                    <code>/api/scenarios/schema</code>) — a watch-only scenario is the recommended starting point.
                </div>
                <button v-for="e in filteredEntries" :key="e.id" type="button" class="scenario-row" @click="open(e.id)">
                    <span class="mono scenario-id">{{ e.id }}</span>
                    <span v-if="e.title" class="scenario-title">{{ e.title }}</span>
                    <span class="item-spacer"></span>
                    <span v-if="e.error" class="scenario-error" :title="e.error">invalid</span>
                    <code v-else class="topology-chip">{{ e.topology }}</code>
                </button>
            </section>
            <section v-else class="block-card">
                <div class="block-header">
                    <button type="button" class="theme-toggle" title="back to the scenario list (Esc)" @click="close">←</button>
                    <h2>{{ heading }}</h2>
                    <code class="icon-chip">{{ store.scenarioId }}</code>
                    <span class="item-spacer"></span>
                    <span v-if="staleRun" class="stale-chip" title="the file on disk no longer matches the file this run executed — the report below is about the older version">file changed since this run</span>
                    <span v-if="run" class="run-status" :class="statusClass">{{ run.status }}</span>
                    <button type="button" class="theme-toggle" title="re-read the file from disk" @click="reload">⟳</button>
                    <button type="button" class="trigger-button"
                            :title="running ? 'cancel the active run and start over' : 'run this scenario'"
                            @click="start()">{{ runLabel }}</button>
                </div>
                <div v-if="store.scenarioError" class="player-empty">{{ store.scenarioError }}</div>
                <div v-if="entryError" class="player-validation">
                    <div class="validation-error">✗ this file fails structural validation: {{ entryError }}</div>
                </div>
                <template v-if="scenario">
                    <div v-if="scenario.description" class="docs-description">{{ scenario.description }}</div>
                    <details class="scenario-file"><summary>{ } scenario file</summary>
                        <pre class="mono">{{ store.scenarioRaw }}</pre>
                    </details>
                    <div v-if="mismatch" class="player-interstitial">
                        <span>⚠ {{ mismatchText }}</span>
                    </div>
                    <div v-if="run" class="player-validation">
                        <div v-for="(e, i) in run.validationErrors" :key="i" class="validation-error">✗ {{ e }}</div>
                    </div>
                    <template v-if="setupSteps.length">
                        <h3 class="topo-section">setup</h3>
                        <PlayerStep v-for="s in setupSteps" :key="'su' + s.index" :step="s"/>
                    </template>
                    <template v-if="steps.length">
                        <h3 class="topo-section">steps</h3>
                        <PlayerStep v-for="s in steps" :key="'st' + s.index" :step="s"/>
                    </template>
                    <template v-if="scenario.watch">
                        <h3 class="topo-section">trace</h3>
                        <ScenarioTrace v-if="run && run.watchTrace && run.watchTrace.length" :run="run" :paths="scenario.watch"/>
                        <div v-else class="player-watch">
                            <ScenarioWatchTile v-for="w in scenario.watch" :key="w" :path="w"/>
                        </div>
                    </template>
                    <template v-if="judge.length">
                        <h3 class="topo-section">judge</h3>
                        <div v-for="(j, i) in judge" :key="i" class="judge-row"
                             :class="{ 'judged-ok': tickState(i) === 'ok', 'judged-notok': tickState(i) === 'notOk' }">
                            <button type="button" class="judge-btn ok" :class="{ active: tickState(i) === 'ok' }"
                                    :disabled="!run" title="looks right" @click="tick(i, 'ok')">✓</button>
                            <button type="button" class="judge-btn notok" :class="{ active: tickState(i) === 'notOk' }"
                                    :disabled="!run" title="your call: not ok (a human verdict, not a runner failure)" @click="tick(i, 'notOk')">✗</button>
                            <span class="judge-text">{{ j.text }}</span>
                            <span v-if="tickState(i) === 'notOk'" class="judge-verdict-tag">your verdict</span>
                            <code v-if="j.spec" class="spec-chip">{{ j.spec }}</code>
                        </div>
                    </template>
                    <div v-if="run" class="player-actions">
                        <button type="button" class="theme-toggle" title="markdown for the PR" @click="copyReport">⧉ copy verification report</button>
                    </div>
                </template>
            </section>
        </div>
    `,
};

// ── ⌘K palette: go to any property / scenario / topology. Enter acts (jump / open / switch); for a
//    property in Explore, Ctrl+Enter pins it to the watch panel instead. ───────────────────────────

// Open the topology editor from anywhere (⌘K palette / keybinding): show the topology view, prime the
// draft (new or cloned), then bump the request counter so a mounted TopologyPanel flips to edit mode.
// `mode` is local to TopologyPanel; topologyEditRequest is the minimal cross-component signal that
// carries the "open the editor" intent. Both honor the read-only gate (no editing on a read-only host).
const topologyEditable = () => !(store.topologies && store.topologies.readOnly);
function openNewTopologyEditor() {
    if (!topologyEditable()) return;
    store.view = 'topology';
    newTopologyDraft();
    store.topologyEditRequest++;
}
function openCloneTopologyEditor(id) {
    if (!topologyEditable()) return;
    store.view = 'topology';
    cloneTopologyDraft(id);
    store.topologyEditRequest++;
}

export const Palette = {
    setup() {
        const query = ref('');
        const selected = ref(0);
        const inputEl = ref(null);
        // Nav targets (scenario / topology) sort ahead of properties on a score tie — they're fewer and the
        // headline "go to anything" use; authoring verbs follow them; properties are the long tail.
        const typeRank = t => (t === 'scenario' ? 0 : t === 'topology' ? 1 : t === 'newtopology' || t === 'edittopology' ? 2 : 3);
        // Authoring verbs are offered only on a writable topology workspace (mirrors TopologyPanel.canEdit).
        const canAuthor = computed(() => !(store.topologies && store.topologies.readOnly));
        const entries = computed(() => {
            const tokens = parseFilter(query.value);
            const q = query.value.trim().toLowerCase();
            const out = [];
            // Scenarios — open them in Verify. Topologies — switch the host to them in Explore.
            ((store.scenarios && store.scenarios.scenarios) || []).forEach(s => {
                const label = s.title || s.id;
                if (!q || s.id.toLowerCase().includes(q) || label.toLowerCase().includes(q)) {
                    out.push({ type: 'scenario', key: 'scn:' + s.id, id: s.id, name: s.id, where: label === s.id ? '' : label, score: s.id.toLowerCase().includes(q) ? 0 : 1 });
                }
            });
            ((store.topologies && store.topologies.topologies) || []).forEach(tp => {
                if (!q || tp.id.toLowerCase().includes(q)) {
                    out.push({ type: 'topology', key: 'top:' + tp.id, id: tp.id, name: tp.id, where: (tp.blocks != null ? tp.blocks + ' blocks' : '') + (tp.id === store.topologyName ? ' · current' : ''), score: tp.id.toLowerCase().includes(q) ? 0 : 1 });
                }
            });
            // Authoring verbs (RFC 0013) — only on a writable topology workspace (mirrors the panel's
            // canEdit gate). "new topology" is always offered; one "edit topology: <id>" per file clones it
            // into the editor. Both open the topology view + the editor via openNew/CloneTopologyEditor.
            if (canAuthor.value) {
                if (!q || 'new topology'.includes(q)) {
                    out.push({ type: 'newtopology', key: 'top:new', name: 'new topology', where: 'author a topology', score: 'new topology'.includes(q) ? 0 : 1 });
                }
                ((store.topologies && store.topologies.topologies) || []).forEach(tp => {
                    const label = 'edit topology: ' + tp.id;
                    if (!q || tp.id.toLowerCase().includes(q) || label.includes(q)) {
                        out.push({ type: 'edittopology', key: 'edit:' + tp.id, id: tp.id, name: label, where: 'clone into editor', score: tp.id.toLowerCase().includes(q) ? 0 : 1 });
                    }
                });
            }
            ((store.config && store.config.logicBlocks) || []).forEach(lb => (lb.services || []).forEach(service => {
                serviceMembers(service).forEach(item => {
                    if (matchesFilter(tokens, item, store.values[valueKey(service.id, item.identifier)])) {
                        // Rank: identifier substring < any-name substring < fuzzy-only.
                        const id = item.identifier.toLowerCase();
                        const score = q === '' || id.includes(q) ? 0
                            : `${resolveDisplayName(item)}`.toLowerCase().includes(q) ? 1 : 2;
                        out.push({ type: 'property', key: 'prop:' + service.id + ':' + item.identifier, lb, service, item, score, multiService: (lb.services || []).length > 1 });
                    }
                });
            }));
            out.sort((a, b) => (a.score - b.score) || (typeRank(a.type) - typeRank(b.type)));
            return out.slice(0, 40);
        });
        watch(query, () => { selected.value = 0; });
        const close = () => { store.paletteOpen = false; };
        const jump = entry => {
            if (entry.type === 'scenario') {
                close();
                store.view = 'player';
                openScenario(entry.id);
                location.hash = `#/scenario/${entry.id}`;
                return;
            }
            if (entry.type === 'topology') {
                close();
                if (entry.id === store.topologyName) { store.view = 'topology'; return; }
                if (store.canReset && !store.recycling) switchTopology(entry.id);
                return;
            }
            if (entry.type === 'newtopology') {
                close();
                openNewTopologyEditor();
                return;
            }
            if (entry.type === 'edittopology') {
                close();
                openCloneTopologyEditor(entry.id);
                return;
            }
            const groupKey = (entry.item.presentation && entry.item.presentation.group) || '';
            store.collapsed[collapseKey(entry.lb.name, entry.service.identifier, groupKey)] = false;
            store.filter = '';
            // Jump targets live in the explorer — leave topology/gallery first.
            store.view = 'explorer';
            close();
            nextTick(() => setTimeout(() => {
                const el = document.getElementById(`item-${entry.service.id}-${entry.item.identifier}`);
                if (el) {
                    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
                    el.classList.add('jump-flash');
                    setTimeout(() => el.classList.remove('jump-flash'), 1600);
                    // Land focus on a trivial writable input (number / text) so you can type a value right
                    // away — the common "jump here to set it" intent. Skipped for read-only / struct rows.
                    const input = el.querySelector('input[type="number"]:not([disabled]), input[type="text"]:not([disabled])');
                    if (input) { input.focus(); input.select && input.select(); }
                }
            }, 50));
        };
        const pinEntry = entry => togglePin({ block: entry.lb.name, service: entry.service.identifier, item: entry.item.identifier });
        const pinnedEntry = entry => isPinned({ block: entry.lb.name, service: entry.service.identifier, item: entry.item.identifier });
        const onKey = e => {
            if (e.key === 'ArrowDown') { selected.value = Math.min(selected.value + 1, entries.value.length - 1); e.preventDefault(); }
            else if (e.key === 'ArrowUp') { selected.value = Math.max(selected.value - 1, 0); e.preventDefault(); }
            else if (e.key === 'Enter') {
                const entry = entries.value[selected.value];
                if (entry) {
                    // Pin (Ctrl/⌘+Enter) only means something for a property in Explore — it pins to the watch
                    // panel. Otherwise (Verify, or a scenario/topology entry) Ctrl+Enter just acts like Enter.
                    if ((e.ctrlKey || e.metaKey) && store.view === 'explorer' && entry.type === 'property') pinEntry(entry);
                    else jump(entry);
                }
            } else if (e.key === 'Escape') {
                close();
            }
        };
        onMounted(() => {
            inputEl.value && inputEl.value.focus();
            // Ensure the scenario / topology lists are present so the palette can offer them from anywhere
            // (init loads them, but the palette may open before that settles).
            if (!(store.topologies && store.topologies.topologies)) loadTopologies();
        });
        const valuePreview = entry => formatValue(
            store.values[valueKey(entry.service.id, entry.item.identifier)],
            (entry.item.presentation || {}).decimals ?? null);
        return { query, selected, entries, inputEl, onKey, jump, pinEntry, pinnedEntry, close, valuePreview, store, modKey: MOD_KEY };
    },
    template: `
        <div class="palette-backdrop" @click.self="close">
            <div class="palette" @keydown="onKey">
                <input ref="inputEl" type="text" class="palette-input" placeholder="go to a property, scenario, or topology — name · name:value · >50"
                       :value="query" @input="query = $event.target.value">
                <div class="palette-results">
                    <div v-for="(e, i) in entries" :key="e.key"
                         class="palette-row" :class="{ selected: i === selected }"
                         @click="jump(e)" @mouseenter="selected = i">
                        <span class="palette-kind" :class="'kind-' + e.type" :title="e.type">{{ e.type === 'scenario' ? '▶' : e.type === 'topology' ? '⛁' : e.type === 'newtopology' ? '＋' : e.type === 'edittopology' ? '✎' : '◦' }}</span>
                        <template v-if="e.type === 'property'">
                            <span class="mono palette-name">{{ e.item.identifier }}</span>
                            <span class="palette-where">{{ e.lb.name }}<template v-if="e.multiService"> · {{ e.service.identifier }}</template><template v-if="e.item.presentation?.group"> · {{ e.item.presentation.group }}</template></span>
                            <span class="item-spacer"></span>
                            <span v-if="pinnedEntry(e)" class="palette-pinned">◆</span>
                            <span class="mono palette-value">{{ valuePreview(e) }}</span>
                        </template>
                        <template v-else>
                            <span class="mono palette-name">{{ e.name }}</span>
                            <span class="palette-where">{{ e.where }}</span>
                            <span class="item-spacer"></span>
                            <span class="palette-type-tag">{{ e.type }}</span>
                        </template>
                    </div>
                    <div v-if="!entries.length" class="palette-empty">no matches</div>
                </div>
                <div class="palette-hint"><kbd>↵</kbd> go<template v-if="store.view === 'explorer'"> · <kbd>{{ modKey }} ↵</kbd> pin property</template> · <kbd>esc</kbd> close</div>
            </div>
        </div>
    `,
};

// ── Keyboard shortcuts: the '?' help overlay + the global keymap (handled in App.onKeydown) ──────────
const KEYBINDINGS = [
    { keys: [PALETTE_KEY_LABEL], desc: 'command palette — go to a property, scenario, or topology' },
    { keys: ['e'], desc: 'Explore' },
    { keys: ['v'], desc: 'Verify' },
    { keys: ['t'], desc: 'topology view' },
    { keys: ['⇧', 'T'], desc: 'new topology editor' },
    { keys: ['[', ']'], desc: 'previous / next — scenario in Verify, topology in Explore' },
    { keys: ['/'], desc: 'focus the filter' },
    { keys: ['b'], desc: 'set baseline (mark the current values)' },
    { keys: ['c'], desc: 'clear all watches' },
    { keys: ['p'], desc: 'pause / resume time' },
    { keys: ['s'], desc: 'step to the next event (stepped clock)' },
    { keys: ['.'], desc: 'advance the clock 1 s (stepped clock)' },
    { keys: ['?'], desc: 'this shortcuts help' },
    { keys: ['Esc'], desc: 'close overlays / clear filter / back to the scenario list' },
];

const KeybindingsHelp = {
    setup() {
        return { store, bindings: KEYBINDINGS, close: () => { store.helpOpen = false; } };
    },
    template: `
        <div class="palette-backdrop" @click.self="close">
            <div class="keyhelp">
                <div class="keyhelp-title">Keyboard shortcuts</div>
                <div class="keyhelp-rows">
                    <div v-for="(b, i) in bindings" :key="i" class="keyhelp-row">
                        <span class="keyhelp-keys"><kbd v-for="k in b.keys" :key="k">{{ k }}</kbd></span>
                        <span class="keyhelp-desc">{{ b.desc }}</span>
                    </div>
                </div>
                <div class="keyhelp-foot">single keys work when you're not typing in a field</div>
            </div>
        </div>
    `,
};

export const App = {
    components: { Rail, BlockCard, WatchPanel, Palette, TopologyPanel, GalleryCard, PlayerPanel, KeybindingsHelp },
    setup() {
        const blocks = computed(() => (store.config && store.config.logicBlocks) || []);
        const sharedLookup = computed(() => buildSharedContractLookup());
        const totals = computed(() => {
            let props = 0;
            blocks.value.forEach(lb => (lb.services || []).forEach(s => {
                props += serviceMembers(s).length;
            }));
            return { blocks: blocks.value.length, props };
        });
        const theme = ref(document.documentElement.dataset.theme || 'dark');
        const toggleTheme = () => {
            theme.value = theme.value === 'dark' ? 'light' : 'dark';
            document.documentElement.dataset.theme = theme.value;
            try { localStorage.setItem('dale.devhost.theme', theme.value); } catch { /* private mode */ }
        };

        // Filter match count ("3 of 25") — same policy the group sections apply.
        const matches = computed(() => {
            if (!filterTokens.value.length) return null;
            let matched = 0;
            blocks.value.forEach(lb => (lb.services || []).forEach(service => {
                serviceMembers(service).forEach(item => {
                    if (itemMatches(service, item)) matched++;
                });
            }));
            return { matched, total: totals.value.props };
        });

        const changedTotal = computed(() => blocks.value.reduce((n, lb) => n + changedCountForBlock(lb), 0));
        const baselineClock = computed(() => {
            const s = store.baselineSeconds;
            return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
        });

        // Keyboard: '/' focuses the filter, 'b' (re)sets the baseline, Escape clears the filter.
        const filterEl = ref(null);
        // Cycle the primary list of the current activity: scenarios in Verify, topologies in Explore.
        const cyclePrimary = dir => {
            if (store.view === 'player') {
                const list = (store.scenarios && store.scenarios.scenarios) || [];
                if (!list.length) return;
                const i = list.findIndex(s => s.id === store.scenarioId);
                const next = list[(((i < 0 ? 0 : i) + dir) % list.length + list.length) % list.length];
                if (next) openScenario(next.id);
            } else {
                const list = (store.topologies && store.topologies.topologies) || [];
                if (list.length < 2 || !store.canReset || store.recycling) return;
                const i = list.findIndex(tp => tp.id === store.topologyName);
                const next = list[(((i < 0 ? 0 : i) + dir) % list.length + list.length) % list.length];
                if (next && next.id !== store.topologyName) switchTopology(next.id);
            }
        };
        const onKeydown = e => {
            const t = e.target;
            const editing = t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT');
            if (e.key === 'Escape') {
                if (store.helpOpen) { store.helpOpen = false; return; }
                if (t === filterEl.value) { store.filter = ''; filterEl.value.blur(); return; }
                // In Verify with a scenario open, Esc backs out to the scenario list (same as the ← button).
                if (store.view === 'player' && store.scenarioId && !editing) { closeScenario(); return; }
            }
            if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
                e.preventDefault();
                store.paletteOpen = !store.paletteOpen;
                return;
            }
            // Single-key shortcuts: only when not typing in a field and no Ctrl/⌘/Alt held (Shift is fine —
            // '?' is Shift+/). Discoverable via the '?' overlay.
            if (editing || e.ctrlKey || e.metaKey || e.altKey) return;
            switch (e.key) {
                case '?': e.preventDefault(); store.helpOpen = !store.helpOpen; break;
                case '/': e.preventDefault(); filterEl.value && filterEl.value.focus(); break;
                case 'e': goExplore(); break;
                case 'v': goVerify(); break;
                case 't': setView('topology'); break;
                case 'T': openNewTopologyEditor(); break;
                case 'b': setBaseline(); break;
                case 'c': clearPins(); break;
                case 'p': store.paused ? resumeHost() : pauseHost(); break;
                case 's': if (store.stepped && !store.runActive) stepHost(); break;
                case '.': if (store.stepped && !store.runActive) advanceHost(1); break;
                case '[': cyclePrimary(-1); break;
                case ']': cyclePrimary(1); break;
            }
        };
        onMounted(() => window.addEventListener('keydown', onKeydown));
        onUnmounted(() => window.removeEventListener('keydown', onKeydown));

        // setView toggles a context view (topology, gallery) against the 'explorer' default — used by the
        // context-zone chips, not the primary nav. (Explore / Verify are goExplore / goVerify below.)
        const setView = v => { store.view = store.view === v ? 'explorer' : v; };
        // Two-zone nav (RFC 0012 §3): Explore and Verify are the two activities. Verify is the scenario
        // player (keeps the #/scenario deep link); Explore is the default browse surface. Topology and
        // gallery are reached from the context chip / overflow, not the primary nav.
        const goExplore = () => {
            // Clear any #/scenario deep link whenever returning to Explore — including via a topology/gallery
            // detour (where store.view is no longer 'player') — so a reload doesn't silently restore Verify.
            if (location.hash) location.hash = '';
            store.view = 'explorer';
        };
        const goVerify = () => {
            if (store.view === 'player') return;
            store.view = 'player';
            location.hash = store.scenarioId ? `#/scenario/${store.scenarioId}` : '#/scenarios';
        };
        const confirmReset = () => {
            resetHost();
        };
        // Virtual clock readout for the stepped-mode control cluster (HH:MM:SS of virtual time; the clock
        // starts at the fixed epoch so this reads as elapsed for a typical session).
        const steppedClock = computed(() => {
            if (!store.virtualTime) return '—';
            try { return new Date(store.virtualTime).toISOString().slice(11, 19); }
            catch { return '—'; }
        });
        return {
            store, blocks, sharedLookup, totals, theme, toggleTheme, matches, changedTotal,
            baselineClock, filterEl, setBaseline, clearBaseline, pauseHost, resumeHost,
            confirmReset, setView, goExplore, goVerify, stepHost, advanceHost, switchClockMode, steppedClock, paletteKeyLabel: PALETTE_KEY_LABEL,
        };
    },
    template: `
        <div class="app">
            <header class="topbar">
                <span class="brand">DALE DevHost</span>
                <nav class="nav-seg">
                    <button type="button" class="nav-tab" :class="{ active: store.view !== 'player' }"
                            title="Explore — browse blocks, drive values, watch live state (e)" @click="goExplore">Explore</button>
                    <button type="button" class="nav-tab" :class="{ active: store.view === 'player' }"
                            title="Verify — run a scenario and review its trace (v)" @click="goVerify">Verify</button>
                </nav>
                <div class="context-zone">
                    <button v-if="store.topologyName" type="button" class="topology-chip-btn" :class="{ active: store.view === 'topology', recycling: store.recycling }"
                            :disabled="store.recycling"
                            :title="store.recycling ? 'recycling the host — please wait' : (store.view === 'topology' ? 'close the topology view (t)' : 'topology ' + store.topologyName + ' — view blocks/links and switch (t)')"
                            @click="setView('topology')">
                        <span v-if="store.recycling"><span class="recycling-spin">♻</span> recycling…</span>
                        <span v-else>⛁ {{ store.topologyName }} ▾</span>
                    </button>
                    <span v-if="store.stepped" class="stepped-chip" :class="{ 'run-owned': store.runActive }"
                          :title="store.runActive ? 'a scenario run owns the virtual clock — manual stepping is paused until the run finishes' : 'deterministic stepping (dale dev --stepped) — the virtual clock advances only when you step it (in Explore) or a scenario runs.'">
                        <span class="stepped-clock" title="virtual clock">⏱ t={{ steppedClock }}</span>
                        <span v-if="store.runActive" class="run-owned-note" title="manual stepping is paused while a scenario run drives the clock">▶ run owns the clock</span>
                    </span>
                    <button v-if="store.canReset" type="button" class="theme-toggle clock-mode-toggle" :disabled="store.recycling"
                            :title="store.stepped ? 'switch to a real-clock host — live wall-clock timers (recycles the host)' : 'switch to a stepped host — deterministic virtual clock (recycles the host)'"
                            @click="switchClockMode(!store.stepped)">⏱ {{ store.stepped ? 'stepped' : 'real-clock' }}</button>
                    <button v-if="!store.paused" type="button" class="theme-toggle"
                            title="pause time-driven activity — timers hold, writes still work (p)" @click="pauseHost">⏸</button>
                    <span v-else class="paused-chip">
                        <span>⏸ paused</span>
                        <button type="button" title="resume — held timers replay (p)" @click="resumeHost">▶</button>
                    </span>
                    <button type="button" class="theme-toggle" :disabled="!store.canReset"
                            :title="store.canReset ? 'recycle the host — fresh start without leaving the browser' : 'reset needs a supervised host (DevHostWebRunner.RunAsync with a host factory)'"
                            @click="confirmReset">↻</button>
                    <button type="button" class="theme-toggle" :title="'go to a property, scenario, or topology (' + paletteKeyLabel + ')'" @click="store.paletteOpen = true">{{ paletteKeyLabel }}</button>
                    <button type="button" class="theme-toggle" title="keyboard shortcuts (press ?)" @click="store.helpOpen = true">?</button>
                    <button type="button" class="theme-toggle" :class="{ 'view-active': store.view === 'gallery' }"
                            :title="store.view === 'gallery' ? 'close the gallery' : 'gallery — how authored presentation renders, on sample values'"
                            @click="setView('gallery')">▦</button>
                    <span class="conn" :class="store.connected ? 'connected' : 'disconnected'">
                        <span class="conn-dot"></span>{{ store.connected ? 'live' : 'disconnected' }}
                    </span>
                    <button type="button" class="theme-toggle" :title="'switch to ' + (theme === 'dark' ? 'light' : 'dark')"
                            @click="toggleTheme">{{ theme === 'dark' ? '☾' : '☀' }}</button>
                </div>
            </header>
            <div v-if="store.view === 'explorer'" class="workspace-bar">
                <span class="filter-wrap">
                    <input ref="filterEl" type="text" class="filter-input" :value="store.filter"
                           placeholder="filter · name:value · >50"
                           title="filter properties — press / to focus"
                           @input="store.filter = $event.target.value">
                    <span v-if="matches" class="filter-count">{{ matches.matched }} of {{ matches.total }}</span>
                    <kbd v-else>/</kbd>
                </span>
                <button v-if="!store.baseline" type="button" class="theme-toggle" title="snapshot a baseline — changed values light up (b)"
                        @click="setBaseline">⚑ baseline</button>
                <span v-else class="baseline-chip">
                    <span>⚑ {{ baselineClock }} · {{ changedTotal }} changed</span>
                    <button type="button" title="re-snapshot (b)" @click="setBaseline">↺</button>
                    <button type="button" title="clear baseline" @click="clearBaseline">✕</button>
                </span>
                <span class="counts">{{ totals.blocks }} blocks · {{ totals.props }} properties</span>
                <span class="ws-spacer"></span>
                <span v-if="store.stepped && !store.runActive" class="step-controls">
                    <button type="button" title="advance the virtual clock to the next scheduled event (s)" @click="stepHost">↦ step</button>
                    <button type="button" title="advance the virtual clock 1 second (.)" @click="advanceHost(1)">+1s</button>
                    <button type="button" title="advance the virtual clock 10 seconds" @click="advanceHost(10)">+10s</button>
                </span>
            </div>
            <div v-if="store.error" class="error-toast">{{ store.error }}</div>
            <div v-if="store.loading" class="loading">Loading configuration…</div>
            <div v-else-if="store.view === 'topology'" class="layout">
                <main class="content">
                    <TopologyPanel/>
                </main>
            </div>
            <div v-else-if="store.view === 'gallery'" class="layout">
                <Rail/>
                <main class="content">
                    <GalleryCard v-for="lb in blocks" :key="lb.id" :lb="lb"/>
                </main>
            </div>
            <div v-else-if="store.view === 'player'" class="layout">
                <main class="content">
                    <PlayerPanel/>
                </main>
            </div>
            <div v-else class="layout">
                <Rail/>
                <main class="content">
                    <BlockCard v-for="lb in blocks" :key="lb.id" :lb="lb" :sharedLookup="sharedLookup"/>
                </main>
                <WatchPanel/>
            </div>
            <div v-if="store.recycling" class="recycling-overlay">
                <div class="recycling-card"><span class="recycling-spin">♻</span> recycling the host… <span class="recycling-sub">rebuilding the network on a fresh clock</span></div>
            </div>
            <Palette v-if="store.paletteOpen"/>
            <KeybindingsHelp v-if="store.helpOpen"/>
        </div>
    `,
};
