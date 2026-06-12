// All Explorer components. Plain-object Vue components with template strings (runtime-compiled —
// the no-build substrate, see CLAUDE.md). Conventions: components render from the store and
// format.js policy helpers; writable controls keep a local draft + dirty flag so incoming live
// updates never clobber an edit (the R0 guarantee, expressed the Vue way: the live value only
// flows into the control while it is not dirty).

import { computed, nextTick, onMounted, onUnmounted, ref, watch } from './vue.esm-browser.prod.js';
import {
    buildVerificationReport, cssGroupKey, defaultOpen, describeType, effectiveType, enumDisplay,
    enumMembers, formatTemporal, formatValue, gallerySamples, GROUP_LABELS, groupItems, isNullable,
    isWritable, matchesFilter, orderedGroupKeys, parseFilter, parseNamePath, presentationFacts,
    resolveDisplayName, resolveUnit, sampleJson, severityFor, STEP_GLYPHS,
} from './format.js';
import {
    applyScenario, baselineDelta, buildSharedContractLookup, changedCountForBlock,
    changedSinceBaseline, clearBaseline, closeScenario, collapseKey, connectionsForLb, halKey,
    isPinned, judgeKey, openScenario, pauseHost, resetHost, resumeHost, setAnalogInput, setBaseline,
    setDigitalInput, setJudgeTick, setProperty, showError, store, toggleCollapsed, togglePin,
    valueKey,
} from './store.js';

// Filter tokens, shared by every component that narrows to matches.
const filterTokens = computed(() => parseFilter(store.filter));

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
    return badges;
}

// ── value rendering ─────────────────────────────────────────────────────────────

export const ValueCell = {
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
        return { display, severity, flashing, writeOnly, isStatus };
    },
    template: `
        <span v-if="isStatus" class="severity-pill" :class="[severity, { updated: flashing }]">{{ display }}</span>
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
    components: { ValueCell, NumberControl, TextControl, EnumSelect, BoolToggle, TriggerButton, SecretControl, JsonEditor, DocsRow, StructViewer },
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
        const docsOpen = ref(false);
        const editorOpen = ref(false);
        const viewerOpen = ref(false);
        return { controlKind, docsOpen, editorOpen, viewerOpen, writable, isStruct, isStatus, hidden, unit, writeOnly, showStructEdit, pinned, togglePinRow, changed };
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
            const collect = (items, kind) => (items || []).forEach(item => {
                if (item.presentation && item.presentation.importance === 'Primary') {
                    entries.push({ service, item: { ...item, _kind: kind } });
                }
            });
            collect(service.serviceProperties, 'property');
            collect(service.serviceMeasuringPoints, 'measuringPoint');
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
                short: type === 'DigitalInput' ? 'DI' : type === 'DigitalOutput' ? 'DO' : type === 'AnalogInput' ? 'AI' : 'AO',
                spId, svcId, cId, sharedWith,
            };
        }).filter(Boolean);

        const halValue = (kindShort, c) => store.hal[halKey(kindShort.toLowerCase(), c.spId, c.svcId, c.cId)];
        const onDi = (c, e) => setDigitalInput(c.spId, c.svcId, c.cId, e.target.checked);
        const onAi = (c, e) => setAnalogInput(c.spId, c.svcId, c.cId, e.target.value);
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
                <span class="contract-type-badge" :class="c.short.toLowerCase()">{{ c.short }}</span>
                <span class="mono io-name">{{ c.id }}</span>
                <span v-if="c.sharedWith.length" class="shared-badge">shared with {{ c.sharedWith.join(', ') }}</span>
                <span class="item-spacer"></span>
                <input v-if="c.short === 'DI'" class="toggle" type="checkbox" :checked="!!halValue('di', c)" @change="onDi(c, $event)">
                <input v-else-if="c.short === 'AI'" type="number" step="0.1" :value="halValue('ai', c) ?? 0"
                       @keydown.enter="onAi(c, $event); $event.target.blur()" @blur="onAi(c, $event)">
                <span v-else class="value-chip">{{ fmt(halValue(c.short.toLowerCase(), c) ?? (c.short === 'DO' ? false : 0)) }}</span>
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
                (s.serviceProperties || []).forEach(p => { total++; if (isWritable(p)) writable++; });
                total += (s.serviceMeasuringPoints || []).length;
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
        const count = lb => (lb.services || []).reduce(
            (n, s) => n + (s.serviceProperties || []).length + (s.serviceMeasuringPoints || []).length, 0);
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
    components: { ValueCell, NumberControl, TextControl, EnumSelect, BoolToggle, TriggerButton, SecretControl, StructViewer },
    props: ['entry'],
    setup(props) {
        // Resolve the name-path pin against the current topology; null → tombstone.
        const resolved = computed(() => {
            const lb = (store.config && store.config.logicBlocks || []).find(b => b.name === props.entry.block);
            const service = lb && (lb.services || []).find(s => s.identifier === props.entry.service);
            if (!service) return null;
            const prop = (service.serviceProperties || []).find(p => p.identifier === props.entry.item);
            const mp = !prop && (service.serviceMeasuringPoints || []).find(p => p.identifier === props.entry.item);
            if (!prop && !mp) return null;
            return { lb, service, item: { ...(prop || mp), _kind: prop ? 'property' : 'measuringPoint' } };
        });
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
        return { resolved, controlKind, changed, delta, unpin, isStruct, viewerOpen };
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
        const resolveKind = entry => {
            const lb = (store.config && store.config.logicBlocks || []).find(b => b.name === entry.block);
            const service = lb && (lb.services || []).find(s => s.identifier === entry.service);
            const prop = service && (service.serviceProperties || []).find(p => p.identifier === entry.item);
            return prop && isWritable(prop) ? 'drive' : 'observe';
        };
        const drive = computed(() => store.pins.filter(p => resolveKind(p) === 'drive'));
        const observe = computed(() => store.pins.filter(p => resolveKind(p) !== 'drive'));
        const empty = computed(() => store.pins.length === 0);
        return { store, drive, observe, empty };
    },
    template: `
        <aside class="watch" :class="{ empty }">
            <template v-if="empty">
                <span class="watch-hint">pin to watch · ◆</span>
            </template>
            <template v-else>
                <div class="watch-header">watch · {{ store.pins.length }}</div>
                <div v-if="drive.length" class="watch-section">drive</div>
                <WatchTile v-for="p in drive" :key="p.block + '/' + p.service + '/' + p.item" :entry="p"/>
                <div v-if="observe.length" class="watch-section">observe</div>
                <WatchTile v-for="p in observe" :key="p.block + '/' + p.service + '/' + p.item" :entry="p"/>
            </template>
        </aside>
    `,
};

// ── topology panel: the read-only setup view — what runs, how it is wired, where IO lands ──────

export const TopologyPanel = {
    setup() {
        const blocks = computed(() => (store.config && store.config.logicBlocks) || []);
        const links = computed(() => (store.config && store.config.interfaceMappings) || []);
        const providers = computed(() => (store.config && store.config.serviceProviders) || []);
        const counts = lb => {
            let writable = 0, total = 0;
            (lb.services || []).forEach(s => {
                (s.serviceProperties || []).forEach(p => { total++; if (isWritable(p)) writable++; });
                total += (s.serviceMeasuringPoints || []).length;
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
                    id: cm.contractIdentifier,
                    short: type === 'DigitalInput' ? 'DI' : type === 'DigitalOutput' ? 'DO' : type === 'AnalogInput' ? 'AI' : 'AO',
                    endpoint: `${cm.mappedServiceProviderIdentifier} / ${cm.mappedServiceIdentifier} / ${cm.mappedContractIdentifier}`,
                };
            });
        };
        return { store, blocks, links, providers, counts, contractRows };
    },
    template: `
        <div class="topology-panel">
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
            </section>
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
        const displayName = (props.item.presentation || {}).displayName || null;
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
        return { glyph, elapsed };
    },
    template: `
        <div class="player-step" :class="step.status">
            <span class="step-glyph">{{ glyph }}</span>
            <code class="step-kind">{{ step.kind }}</code>
            <span class="mono step-target">{{ step.target }}</span>
            <span v-if="step.label" class="step-label">{{ step.label }}</span>
            <code v-if="step.spec" class="spec-chip">{{ step.spec }}</code>
            <span class="item-spacer"></span>
            <span v-if="step.detail" class="step-detail" :title="step.detail">{{ step.detail }}</span>
            <span v-if="elapsed" class="step-elapsed">{{ elapsed }}</span>
        </div>
    `,
};

export const PlayerPanel = {
    components: { PlayerStep, ScenarioWatchTile },
    setup() {
        const entries = computed(() => (store.scenarios && store.scenarios.scenarios) || []);
        const directory = computed(() => (store.scenarios && store.scenarios.directory) || '');
        const scenario = computed(() => store.scenario);
        const run = computed(() => store.run && store.run.scenarioId === store.scenarioId ? store.run : null);
        const running = computed(() => !!run.value && run.value.status === 'running');

        // Topology guard: the pre-run mismatch warning uses the live host topology; a blocked run
        // reports it server-side too (status topologyMismatch).
        const mismatch = computed(() => {
            if (!scenario.value) return false;
            return scenario.value.topology !== store.topologyName;
        });
        const mismatchText = computed(() => {
            if (!scenario.value) return '';
            const host = store.topologyName ? `'${store.topologyName}'` : 'no declared topology';
            return `this scenario expects topology '${scenario.value.topology}' — the host runs ${host}`;
        });

        // Before the first run: pending-shaped rows from the file, so the working set is visible
        // immediately. After: the server report is the truth. Defensive against structurally invalid
        // files — the list keeps them clickable on purpose (the error panel explains them).
        const fileSteps = section => {
            const raw = scenario.value && Array.isArray(scenario.value[section]) ? scenario.value[section] : [];
            return raw.map((s, i) => ({
                index: i,
                kind: s.set !== undefined ? 'set'
                    : s.digitalInput ? 'digitalInput'
                    : s.analogInput ? 'analogInput'
                    : s.waitUntil ? 'waitUntil'
                    : s.wait ? 'wait' : 'unknown',
                label: s.label,
                spec: s.spec,
                target: s.set !== undefined ? s.set
                    : s.digitalInput ? `${s.digitalInput.block}.${s.digitalInput.contract}`
                    : s.analogInput ? `${s.analogInput.block}.${s.analogInput.contract}`
                    : s.waitUntil ? s.waitUntil.property
                    : s.wait ? `${s.wait.seconds} s` : '?',
                status: 'pending',
            }));
        };
        const setupSteps = computed(() => run.value ? run.value.setup : fileSteps('setup'));
        const steps = computed(() => run.value ? run.value.steps : fileSteps('steps'));
        const judge = computed(() => run.value ? run.value.judge
            : ((scenario.value && scenario.value.judge) || []).map(j => ({ text: j.text, spec: j.spec, status: 'requiresHuman' })));

        const statusClass = computed(() => run.value ? run.value.status : 'none');
        const runLabel = computed(() => running.value ? '⟳ restart' : run.value ? '↻ run again' : '▶ run');
        const heading = computed(() => (scenario.value && scenario.value.title) || store.scenarioId);
        // The structural parse error from discovery, when this file is broken — kept clickable on
        // purpose; the Player explains instead of crashing on a half-shaped working set.
        const entryError = computed(() => {
            const entries2 = (store.scenarios && store.scenarios.scenarios) || [];
            const entry = entries2.find(e => e.id === store.scenarioId);
            return entry ? entry.error : null;
        });
        const start = force => applyScenario(store.scenarioId, { restart: running.value, force });
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

        return {
            store, entries, directory, scenario, run, running, mismatch, mismatchText, setupSteps,
            steps, judge, statusClass, runLabel, heading, entryError, start, tick, tickState,
            copyReport, reload, open: openScenario, close: closeScenario,
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
                <div v-if="!entries.length" class="player-empty">
                    No scenario files. Create <code>scenarios/&lt;id&gt;.scenario.json</code> (schema:
                    <code>/api/scenarios/schema</code>) — a watch-only scenario is the recommended starting point.
                </div>
                <button v-for="e in entries" :key="e.id" type="button" class="scenario-row" @click="open(e.id)">
                    <span class="mono scenario-id">{{ e.id }}</span>
                    <span v-if="e.title" class="scenario-title">{{ e.title }}</span>
                    <span class="item-spacer"></span>
                    <span v-if="e.error" class="scenario-error" :title="e.error">invalid</span>
                    <code v-else class="topology-chip">{{ e.topology }}</code>
                </button>
            </section>
            <section v-else class="block-card">
                <div class="block-header">
                    <button type="button" class="theme-toggle" title="all scenarios" @click="close">←</button>
                    <h2>{{ heading }}</h2>
                    <code class="icon-chip">{{ store.scenarioId }}</code>
                    <span class="item-spacer"></span>
                    <span v-if="run" class="run-status" :class="statusClass">{{ run.status }}</span>
                    <button type="button" class="theme-toggle" title="re-read the file from disk" @click="reload">⟳</button>
                    <button v-if="!mismatch" type="button" class="trigger-button"
                            :title="running ? 'cancel the active run and start over' : 'run this scenario'"
                            @click="start(false)">{{ runLabel }}</button>
                </div>
                <div v-if="store.scenarioError" class="player-empty">{{ store.scenarioError }}</div>
                <div v-if="entryError" class="player-validation">
                    <div class="validation-error">✗ this file fails structural validation: {{ entryError }}</div>
                </div>
                <template v-if="scenario">
                    <div v-if="scenario.description" class="docs-description">{{ scenario.description }}</div>
                    <div v-if="mismatch" class="player-interstitial">
                        <span>⚠ {{ mismatchText }}</span>
                        <button type="button" @click="start(true)">run anyway</button>
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
                        <h3 class="topo-section">watch</h3>
                        <div class="player-watch">
                            <ScenarioWatchTile v-for="w in scenario.watch" :key="w" :path="w"/>
                        </div>
                    </template>
                    <template v-if="judge.length">
                        <h3 class="topo-section">judge</h3>
                        <div v-for="(j, i) in judge" :key="i" class="judge-row">
                            <button type="button" class="judge-btn ok" :class="{ active: tickState(i) === 'ok' }"
                                    :disabled="!run" title="looks right" @click="tick(i, 'ok')">✓</button>
                            <button type="button" class="judge-btn notok" :class="{ active: tickState(i) === 'notOk' }"
                                    :disabled="!run" title="not ok" @click="tick(i, 'notOk')">✗</button>
                            <span class="judge-text">{{ j.text }}</span>
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

// ── Ctrl+K palette: type to find any property, Enter jumps to it, Ctrl+Enter pins it ───────────

export const Palette = {
    setup() {
        const query = ref('');
        const selected = ref(0);
        const inputEl = ref(null);
        const entries = computed(() => {
            const tokens = parseFilter(query.value);
            const q = query.value.trim().toLowerCase();
            const out = [];
            ((store.config && store.config.logicBlocks) || []).forEach(lb => (lb.services || []).forEach(service => {
                [...(service.serviceProperties || []), ...(service.serviceMeasuringPoints || [])].forEach(item => {
                    if (matchesFilter(tokens, item, store.values[valueKey(service.id, item.identifier)])) {
                        // Rank: identifier substring < any-name substring < fuzzy-only.
                        const id = item.identifier.toLowerCase();
                        const score = q === '' || id.includes(q) ? 0
                            : `${resolveDisplayName(item)}`.toLowerCase().includes(q) ? 1 : 2;
                        out.push({ lb, service, item, score, multiService: (lb.services || []).length > 1 });
                    }
                });
            }));
            out.sort((a, b) => a.score - b.score);
            return out.slice(0, 40);
        });
        watch(query, () => { selected.value = 0; });
        const close = () => { store.paletteOpen = false; };
        const jump = entry => {
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
                    if (e.ctrlKey || e.metaKey) pinEntry(entry);
                    else jump(entry);
                }
            } else if (e.key === 'Escape') {
                close();
            }
        };
        onMounted(() => inputEl.value && inputEl.value.focus());
        const valuePreview = entry => formatValue(
            store.values[valueKey(entry.service.id, entry.item.identifier)],
            (entry.item.presentation || {}).decimals ?? null);
        return { query, selected, entries, inputEl, onKey, jump, pinEntry, pinnedEntry, close, valuePreview };
    },
    template: `
        <div class="palette-backdrop" @click.self="close">
            <div class="palette" @keydown="onKey">
                <input ref="inputEl" type="text" class="palette-input" placeholder="jump to property — name · name:value · >50"
                       :value="query" @input="query = $event.target.value">
                <div class="palette-results">
                    <div v-for="(e, i) in entries" :key="e.service.id + e.item.identifier"
                         class="palette-row" :class="{ selected: i === selected }"
                         @click="jump(e)" @mouseenter="selected = i">
                        <span class="mono palette-name">{{ e.item.identifier }}</span>
                        <span class="palette-where">{{ e.lb.name }}<template v-if="e.multiService"> · {{ e.service.identifier }}</template><template v-if="e.item.presentation?.group"> · {{ e.item.presentation.group }}</template></span>
                        <span class="item-spacer"></span>
                        <span v-if="pinnedEntry(e)" class="palette-pinned">◆</span>
                        <span class="mono palette-value">{{ valuePreview(e) }}</span>
                    </div>
                    <div v-if="!entries.length" class="palette-empty">no matches</div>
                </div>
                <div class="palette-hint"><kbd>↵</kbd> jump · <kbd>ctrl ↵</kbd> pin · <kbd>esc</kbd> close</div>
            </div>
        </div>
    `,
};

export const App = {
    components: { Rail, BlockCard, WatchPanel, Palette, TopologyPanel, GalleryCard, PlayerPanel },
    setup() {
        const blocks = computed(() => (store.config && store.config.logicBlocks) || []);
        const sharedLookup = computed(() => buildSharedContractLookup());
        const totals = computed(() => {
            let props = 0;
            blocks.value.forEach(lb => (lb.services || []).forEach(s => {
                props += (s.serviceProperties || []).length + (s.serviceMeasuringPoints || []).length;
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
                [...(service.serviceProperties || []), ...(service.serviceMeasuringPoints || [])].forEach(item => {
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
        const onKeydown = e => {
            const t = e.target;
            const editing = t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT');
            if (e.key === 'Escape' && t === filterEl.value) {
                store.filter = '';
                filterEl.value.blur();
                return;
            }
            if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
                e.preventDefault();
                store.paletteOpen = !store.paletteOpen;
                return;
            }
            if (editing) return;
            if (e.key === '/') {
                e.preventDefault();
                filterEl.value && filterEl.value.focus();
            } else if (e.key === 'b') {
                setBaseline();
            }
        };
        onMounted(() => window.addEventListener('keydown', onKeydown));
        onUnmounted(() => window.removeEventListener('keydown', onKeydown));

        // Four top-level views; each button toggles its view against the explorer default. The player
        // additionally keeps the deep-link hash (RFC 0006 #/scenario/{id}) in sync.
        const setView = v => { store.view = store.view === v ? 'explorer' : v; };
        const toggleScenarios = () => {
            if (store.view === 'player') {
                store.view = 'explorer';
                if (location.hash) location.hash = '';
                return;
            }
            store.view = 'player';
            location.hash = store.scenarioId ? `#/scenario/${store.scenarioId}` : '#/scenarios';
        };
        const confirmReset = () => {
            resetHost();
        };
        return {
            store, blocks, sharedLookup, totals, theme, toggleTheme, matches, changedTotal,
            baselineClock, filterEl, setBaseline, clearBaseline, pauseHost, resumeHost,
            confirmReset, setView, toggleScenarios,
        };
    },
    template: `
        <div class="app">
            <header class="topbar">
                <span class="brand">DALE DevHost</span>
                <code v-if="store.topologyName" class="topology-chip">{{ store.topologyName }}</code>
                <span class="counts">{{ totals.blocks }} blocks · {{ totals.props }} properties</span>
                <span class="filter-wrap">
                    <input ref="filterEl" type="text" class="filter-input" :value="store.filter"
                           placeholder="filter · name:value · >50"
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
                <button v-if="!store.paused" type="button" class="theme-toggle"
                        title="pause time-driven activity — timers hold, writes still work"
                        @click="pauseHost">⏸ pause</button>
                <span v-else class="paused-chip">
                    <span>⏸ paused</span>
                    <button type="button" title="resume — held timers replay" @click="resumeHost">▶</button>
                </span>
                <button type="button" class="theme-toggle" :disabled="!store.canReset"
                        :title="store.canReset ? 'recycle the host — fresh start without leaving the browser' : 'reset needs a supervised host (DevHostWebRunner.RunAsync with a host factory)'"
                        @click="confirmReset">↻ reset</button>
                <button type="button" class="theme-toggle" :class="{ 'view-active': store.view === 'topology' }"
                        :title="store.view === 'topology' ? 'back to the explorer' : 'topology — blocks, links, mocked IO'"
                        @click="setView('topology')">⛁ topology</button>
                <button type="button" class="theme-toggle" :class="{ 'view-active': store.view === 'gallery' }"
                        :title="store.view === 'gallery' ? 'back to the explorer' : 'gallery — how authored presentation renders, on sample values'"
                        @click="setView('gallery')">▦ gallery</button>
                <button type="button" class="theme-toggle" :class="{ 'view-active': store.view === 'player' }"
                        :title="store.view === 'player' ? 'back to the explorer' : 'scenarios — staged verification runs (RFC 0006)'"
                        @click="toggleScenarios">▶ scenarios</button>
                <span class="conn" :class="store.connected ? 'connected' : 'disconnected'">
                    <span class="conn-dot"></span>{{ store.connected ? 'live' : 'disconnected' }}
                </span>
                <button type="button" class="theme-toggle" :title="'switch to ' + (theme === 'dark' ? 'light' : 'dark')"
                        @click="toggleTheme">{{ theme === 'dark' ? '☾' : '☀' }}</button>
            </header>
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
            <Palette v-if="store.paletteOpen"/>
        </div>
    `,
};
