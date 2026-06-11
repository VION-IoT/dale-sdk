// All Explorer components. Plain-object Vue components with template strings (runtime-compiled —
// the no-build substrate, see CLAUDE.md). Conventions: components render from the store and
// format.js policy helpers; writable controls keep a local draft + dirty flag so incoming live
// updates never clobber an edit (the R0 guarantee, expressed the Vue way: the live value only
// flows into the control while it is not dirty).

import { computed, ref, watch } from './vue.esm-browser.prod.js';
import {
    cssGroupKey, defaultOpen, describeType, effectiveType, enumDisplay, enumMembers, formatTemporal,
    formatValue, GROUP_LABELS, groupItems, isNullable, isWritable, orderedGroupKeys,
    resolveDisplayName, resolveUnit, sampleJson, severityFor,
} from './format.js';
import {
    buildSharedContractLookup, collapseKey, connectionsForLb, halKey, setAnalogInput,
    setDigitalInput, setProperty, showError, store, toggleCollapsed, valueKey,
} from './store.js';

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
    props: ['service', 'item'],
    setup(props) {
        const live = useLive(props);
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

// Struct / array editor: raw-JSON textarea with a sample template and the full schema on demand.
// The dirty flag protects a half-typed draft from incoming live updates (textareas never sync
// while dirty); a successful Set clears it.
export const JsonEditor = {
    props: ['service', 'item'],
    setup(props) {
        const live = useLive(props);
        const schema = props.item.schema || {};
        const nullable = isNullable(schema);
        const text = ref('');
        const dirty = ref(false);
        const seed = v => v === null || v === undefined ? '' : JSON.stringify(v, null, 2);
        text.value = seed(live.value);
        watch(live, v => { if (!dirty.value) text.value = seed(v); });
        const sample = JSON.stringify(sampleJson(schema), null, 2);
        const schemaJson = JSON.stringify(schema, null, 2);
        const fillTemplate = () => { text.value = sample; dirty.value = true; };
        const commit = () => {
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
        return { text, dirty, commit, setNull, fillTemplate, nullable, sample, schemaJson };
    },
    template: `
        <div class="json-editor">
            <details class="json-help"><summary>example &amp; schema</summary>
                <div class="json-help-body">
                    <pre>{{ sample }}</pre>
                    <button type="button" title="Use as template" @click="fillTemplate">📋</button>
                </div>
                <details class="json-help-schema"><summary>full schema</summary><pre>{{ schemaJson }}</pre></details>
            </details>
            <textarea rows="4" spellcheck="false" class="mono" :value="text" placeholder="(paste / type JSON)"
                      @input="dirty = true; text = $event.target.value"></textarea>
            <div class="json-actions">
                <button type="button" @click="commit">Set JSON</button>
                <button v-if="nullable" type="button" class="null-btn" @click="setNull">×∅</button>
                <span v-if="dirty" class="draft-hint">draft — live updates held</span>
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
    components: { ValueCell, NumberControl, TextControl, EnumSelect, BoolToggle, TriggerButton, SecretControl, JsonEditor, DocsRow },
    props: ['service', 'item'],
    setup(props) {
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
        return { controlKind, docsOpen, editorOpen, writable, isStruct, isStatus, hidden, unit, writeOnly, showStructEdit };
    },
    template: `
        <div class="item" :class="{ 'hidden-importance': hidden }">
            <div class="item-row">
                <button type="button" class="docs-toggle" :class="{ open: docsOpen }" title="docs &amp; schema"
                        @click="docsOpen = !docsOpen">▸</button>
                <span class="item-name mono">{{ item.identifier }}</span>
                <code v-if="unit" class="unit-chip">{{ unit }}</code>
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
                    <button v-if="showStructEdit" type="button" class="edit-toggle"
                            :class="{ open: editorOpen }" @click="editorOpen = !editorOpen">{ } edit</button>
                </template>
            </div>
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
        const collapsed = computed(() => {
            const explicit = store.collapsed[key];
            if (explicit !== undefined) return explicit;
            return !defaultOpen(props.groupKey, props.items.length);
        });
        const toggle = () => toggleCollapsed(key, collapsed.value);
        const label = GROUP_LABELS[props.groupKey] !== undefined ? GROUP_LABELS[props.groupKey] : props.groupKey;
        const css = cssGroupKey(props.groupKey);
        return { collapsed, toggle, label, css };
    },
    template: `
        <div class="group-section" :class="css">
            <button type="button" class="group-header" @click="toggle">
                <span class="chevron" :class="{ open: !collapsed }">▸</span>
                <code class="group-key">{{ label }}</code>
                <span class="group-count">{{ items.length }}</span>
            </button>
            <div v-if="!collapsed" class="group-items">
                <ItemRow v-for="it in items" :key="it.identifier" :service="service" :item="it"/>
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
        return { services, totals, icon, multiService };
    },
    template: `
        <section class="block-card" :id="'block-' + lb.id">
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
        return { blocks, count, select, store };
    },
    template: `
        <nav class="rail">
            <button v-for="lb in blocks" :key="lb.id" type="button" class="rail-item"
                    :class="{ active: store.selectedBlockId === lb.id }" @click="select(lb)">
                <span class="rail-dot"></span>
                <span class="rail-name">{{ lb.name }}</span>
                <span class="rail-count">{{ count(lb) }}</span>
            </button>
        </nav>
    `,
};

export const App = {
    components: { Rail, BlockCard },
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
        return { store, blocks, sharedLookup, totals };
    },
    template: `
        <div class="app">
            <header class="topbar">
                <span class="brand">DALE DevHost</span>
                <code v-if="store.topologyName" class="topology-chip">{{ store.topologyName }}</code>
                <span class="counts">{{ totals.blocks }} blocks · {{ totals.props }} properties</span>
                <span class="conn" :class="store.connected ? 'connected' : 'disconnected'">
                    <span class="conn-dot"></span>{{ store.connected ? 'live' : 'disconnected' }}
                </span>
            </header>
            <div v-if="store.error" class="error-toast">{{ store.error }}</div>
            <div v-if="store.loading" class="loading">Loading configuration…</div>
            <div v-else class="layout">
                <Rail/>
                <main class="content">
                    <BlockCard v-for="lb in blocks" :key="lb.id" :lb="lb" :sharedLookup="sharedLookup"/>
                </main>
            </div>
        </div>
    `,
};
