// Pure schema/presentation/value helpers — no DOM, no store. Ported from the pre-R1 monolithic
// index.html so rendering policy survives the Vue rewrite unchanged.

// Returns the non-null variant of schema.type if it's an array; otherwise schema.type itself.
export function effectiveType(schema) {
    if (!schema || schema.type === undefined) return null;
    if (Array.isArray(schema.type)) {
        return schema.type.find(t => t !== 'null') || null;
    }
    return schema.type;
}

// True if the schema's type is array-form including 'null' (nullable wrapper).
export function isNullable(schema) {
    return !!(schema && Array.isArray(schema.type) && schema.type.includes('null'));
}

// Returns the enum member names from the schema, or null. For nullable enums, strips the trailing null entry.
export function enumMembers(schema) {
    if (!schema || !Array.isArray(schema.enum)) return null;
    return schema.enum.filter(m => m !== null);
}

// Composite type-display string (e.g. "struct Coordinates?", "array<number>", "number (double)").
export function describeType(schema) {
    if (!schema) return 'unknown';
    const t = effectiveType(schema);
    const fmt = schema.format;
    const nullable = isNullable(schema);
    let label;
    if (enumMembers(schema)) {
        label = schema.title ? `enum ${schema.title}` : 'enum';
    } else if (t === 'array') {
        const itemT = effectiveType(schema.items) || '?';
        label = `array<${itemT}>`;
    } else if (t === 'object') {
        label = schema.title ? `struct ${schema.title}` : 'struct';
    } else if (fmt) {
        label = `${t} (${fmt})`;
    } else {
        label = t || 'unknown';
    }
    return nullable ? `${label}?` : label;
}

// Human-readable display name (presentation.displayName || schema.title || identifier).
// The dense row shows the IDENTIFIER (dev-tool policy: tests and IDevHostControl address by
// identifier); the display name lives in the docs expander.
export function resolveDisplayName(item) {
    return (item.presentation && item.presentation.displayName)
        || (item.schema && item.schema.title)
        || item.identifier;
}

// True when schema.title is identity-bearing — the CLR type name of an enum or struct (incl.
// nullable/array wrappers), NOT an authored display label. Mirrors the SDK's HasIdentityBearingTitle
// (PropertyMetadataBuilder): for those types the property-level [Title] is routed to
// presentation.displayName, leaving schema.title carrying the type identity (e.g. "AlarmState").
export function hasIdentityBearingTitle(schema) {
    if (!schema) return false;
    if (enumMembers(schema)) return true;
    const t = effectiveType(schema);
    if (t === 'object') return true;
    if (t === 'array') return hasIdentityBearingTitle(schema.items);
    return false;
}

// The authored, operator-facing title for a property, or null. presentation.displayName always wins
// (where the SDK routes [Title] for enum/struct, and where explicit [Presentation(DisplayName)]
// lands). For scalar types the authored [Title] lands in schema.title instead — surface that too,
// but never the identity-bearing schema.title of an enum/struct (that's the CLR type name, already
// shown via the type label). No identifier fallback: callers show the identifier separately.
export function resolveAuthoredTitle(item) {
    const presentation = item.presentation || {};
    if (presentation.displayName) return presentation.displayName;
    const schema = item.schema || {};
    if (schema.title && !hasIdentityBearingTitle(schema)) return schema.title;
    return null;
}

export function resolveUnit(schema) {
    return (schema && schema['x-unit']) || null;
}

// True if a service property is writable. The schema's readOnly flag overrides; otherwise writable.
export function isWritable(item) {
    return !(item.schema && item.schema.readOnly === true);
}

// Resolve the display label for an enum member from presentation.enumLabels ([EnumLabel]).
// Case-tolerant lookup: wire dictionary keys may be camelCased by the serializer while schema
// enum members keep their C# PascalCase names.
export function enumLabelFor(enumLabels, memberName) {
    if (!enumLabels || memberName === null || memberName === undefined) return null;
    if (Object.prototype.hasOwnProperty.call(enumLabels, memberName)) return enumLabels[memberName];
    const lower = String(memberName).toLowerCase();
    const key = Object.keys(enumLabels).find(k => k.toLowerCase() === lower);
    return key !== undefined ? enumLabels[key] : null;
}

// Dev-tool enum display: "Label (Name)" when a label exists and differs — the wire member name
// must stay visible — plain member name otherwise.
export function enumDisplay(enumLabels, memberName) {
    const label = enumLabelFor(enumLabels, memberName);
    return label && label !== memberName ? `${label} (${memberName})` : String(memberName);
}

// Severity for a status-indicator value via presentation.statusMappings.
export function severityFor(statusMappings, value) {
    if (value === null || value === undefined) return 'neutral';
    const mapping = statusMappings || {};
    return mapping[value] || 'neutral';
}

// Format a value for display. Numbers: presentation.decimals when authored (fixed places), else
// max 3 decimal places. Null/undefined: ∅. Objects/arrays: JSON-stringified with the same rounding.
export function formatValue(value, decimals = null) {
    const dp = decimals !== null && decimals !== undefined && Number.isFinite(decimals) ? decimals : null;
    if (value === null || value === undefined) {
        return '∅';
    }
    if (typeof value === 'number') {
        if (dp !== null) {
            return value.toFixed(dp);
        }
        return Number.isInteger(value) ? value.toString() : value.toFixed(3);
    }
    if (typeof value === 'object') {
        return JSON.stringify(value, (key, v) => typeof v === 'number' && !Number.isInteger(v) ? Number(v.toFixed(dp ?? 3)) : v);
    }
    return String(value);
}

// Format a DateTime / TimeSpan value through dayjs (global, classic script) using the
// Presentation.Format spec. schemaFormat: "date-time" | "duration". presentationFormat: a
// moment-compatible token string, a reserved sentinel ("relative" / "humanize"), or null.
export function formatTemporal(value, schemaFormat, presentationFormat) {
    if (value === null || value === undefined) return '∅';
    if (!window.dayjs) return String(value);

    if (schemaFormat === 'date-time') {
        const d = window.dayjs(value);
        if (!d.isValid()) return String(value);
        if (presentationFormat === 'relative') {
            return d.fromNow();
        }
        return d.format(presentationFormat || 'LLL');
    }

    if (schemaFormat === 'duration') {
        const ms = parseDurationToMs(value);
        if (ms === null) return String(value);
        const dur = window.dayjs.duration(ms);
        if (presentationFormat === 'humanize') {
            return dur.humanize();
        }
        return dur.format(presentationFormat || 'HH:mm:ss');
    }

    return String(value);
}

// Best-effort parse of a TimeSpan/duration wire value into milliseconds. Accepts the .NET
// TimeSpan ToString form ("[-][d.]hh:mm:ss[.fffffff]"), ISO 8601 durations ("PT3H30M"), and
// plain numbers (treated as milliseconds). Returns null when nothing parses.
export function parseDurationToMs(value) {
    if (typeof value === 'number') return value;
    if (typeof value !== 'string') return null;

    if (/^-?P/i.test(value)) {
        const d = window.dayjs.duration(value);
        return (d.isValid && !d.isValid()) ? null : d.asMilliseconds();
    }

    const m = /^(-)?(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?$/.exec(value);
    if (!m) return null;
    const sign = m[1] === '-' ? -1 : 1;
    const days = parseInt(m[2] || '0', 10);
    const hours = parseInt(m[3], 10);
    const minutes = parseInt(m[4], 10);
    const seconds = parseInt(m[5], 10);
    const fractional = m[6] || '';
    const ticks = parseInt((fractional + '0000000').slice(0, 7), 10);
    const fracMs = ticks / 10000;
    const totalMs = days * 86400000 + hours * 3600000 + minutes * 60000 + seconds * 1000 + fracMs;
    return sign * totalMs;
}

// Build a sample JSON value from a schema (best-effort) — the struct/array editor template.
export function sampleJson(schema) {
    if (!schema) return null;
    const t = effectiveType(schema);
    if (Array.isArray(schema.enum)) {
        return schema.enum.find(m => m !== null) ?? null;
    }
    if (t === 'object' && schema.properties) {
        const out = {};
        for (const [k, fieldSchema] of Object.entries(schema.properties)) {
            out[k] = sampleJson(fieldSchema);
        }
        return out;
    }
    if (t === 'array') {
        return [sampleJson(schema.items)];
    }
    if (t === 'boolean') return false;
    if (t === 'integer') return 0;
    if (t === 'number') return 0;
    if (t === 'string') {
        if (schema.format === 'date-time') return new Date().toISOString();
        if (schema.format === 'duration') return 'PT0S';
        return '';
    }
    return null;
}

// ── Filter policy ───────────────────────────────────────────────────────────────
// Whitespace-separated tokens, all must match (AND). Token forms:
//   >N / <N      numeric comparison against the live value
//   name:text    name (identifier / display name / title) substring AND value substring
//   text         substring across identifier, display names, and the formatted live value
export function parseFilter(query) {
    return (query || '').trim().toLowerCase().split(/\s+/).filter(Boolean);
}

// True when every character of needle appears in order in haystack ("soc" ~ "stateofcharge").
function isSubsequence(haystack, needle) {
    let i = 0;
    for (const ch of haystack) {
        if (ch === needle[i]) i++;
        if (i === needle.length) return true;
    }
    return needle.length === 0;
}

export function matchesFilter(tokens, item, live) {
    if (!tokens.length) return true;
    const identifier = item.identifier.toLowerCase();
    const names = [
        item.identifier,
        (item.presentation && item.presentation.displayName) || '',
        (item.schema && item.schema.title) || '',
    ].join(' ').toLowerCase();
    const valueText = live === undefined ? '' : String(formatValue(live)).toLowerCase();
    return tokens.every(tok => {
        if (tok[0] === '>' || tok[0] === '<') {
            const n = parseFloat(tok.slice(1));
            if (Number.isNaN(n) || typeof live !== 'number') return false;
            return tok[0] === '>' ? live > n : live < n;
        }
        const colon = tok.indexOf(':');
        if (colon > 0) {
            const name = tok.slice(0, colon);
            const val = tok.slice(colon + 1);
            return names.includes(name) && (val === '' || valueText.includes(val));
        }
        // Substring across names + formatted value; fuzzy subsequence on the identifier only
        // ("soc" finds StateOfCharge without flooding matches through titles and values).
        return names.includes(tok) || valueText.includes(tok) || isSubsequence(identifier, tok);
    });
}

// ── Gallery sample policy (R2.5) ────────────────────────────────────────────────
// Synthetic values for the presentation preview gallery: representative samples derived purely
// from schema metadata, so authors see how their attributes render without the block publishing
// anything. The slot picks a point in the value space (bounds become low/mid/high, enums walk
// their members) so multi-sample strips and array rows show variety instead of three zeroes.

function scalarSample(schema, slot) {
    const t = effectiveType(schema);
    const members = enumMembers(schema);
    if (members && members.length) {
        const idx = slot === 'low' ? 0 : slot === 'high' ? members.length - 1 : Math.floor((members.length - 1) / 2);
        return members[idx];
    }
    if (t === 'boolean') return slot !== 'low';
    if (t === 'integer' || t === 'number') {
        const min = schema.minimum;
        const max = schema.maximum;
        let v;
        if (min !== undefined && max !== undefined) v = slot === 'low' ? min : slot === 'high' ? max : (min + max) / 2;
        else if (min !== undefined) v = slot === 'low' ? min : min + (slot === 'high' ? 100 : 10);
        else if (max !== undefined) v = slot === 'high' ? max : max - (slot === 'low' ? 100 : 10);
        else v = slot === 'low' ? 0 : slot === 'high' ? 1234.5678 : 42.5;
        return t === 'integer' ? Math.round(v) : v;
    }
    if (t === 'string') {
        if (schema.format === 'date-time') {
            const now = Date.now();
            return new Date(slot === 'low' ? now - 3 * 3600000 : now).toISOString();
        }
        if (schema.format === 'duration') return slot === 'low' ? 'PT12S' : 'PT1H23M45S';
        return 'text sample';
    }
    return null;
}

function objectSample(schema, slot) {
    const out = {};
    for (const [name, fieldSchema] of Object.entries((schema && schema.properties) || {})) {
        const t = effectiveType(fieldSchema);
        if (t === 'object') out[name] = objectSample(fieldSchema, slot);
        else if (t === 'array') out[name] = [scalarSample(fieldSchema.items || {}, slot)];
        else out[name] = scalarSample(fieldSchema, slot);
    }
    return out;
}

// Labeled display samples for one item. Scalars get a chip strip (every enum member / status
// mapping, min·mid·max for bounded numbers, now vs earlier for temporals, the ∅ case when
// nullable); objects get one representative struct, arrays three varied rows.
export function gallerySamples(item) {
    const schema = item.schema || {};
    const presentation = item.presentation || {};
    const t = effectiveType(schema);
    const members = enumMembers(schema);
    const samples = [];

    if (schema.writeOnly === true) {
        return [{ label: 'set', value: '***' }, { label: 'cleared', value: null }];
    }
    if (t === 'object') {
        return [{ label: 'sample', value: objectSample(schema, 'mid') }];
    }
    if (t === 'array') {
        const itemSchema = schema.items || {};
        const make = slot => effectiveType(itemSchema) === 'object' ? objectSample(itemSchema, slot) : scalarSample(itemSchema, slot);
        const rows = ['low', 'mid', 'high'].map(make);
        const distinct = effectiveType(itemSchema) === 'object' ? rows : [...new Map(rows.map(v => [JSON.stringify(v), v])).values()];
        return [{ label: 'sample', value: distinct }];
    }

    if (members) {
        members.forEach(m => samples.push({ label: '', value: m }));
    } else if (presentation.statusMappings) {
        Object.keys(presentation.statusMappings).forEach(k => samples.push({ label: '', value: k }));
    } else if (t === 'boolean') {
        samples.push({ label: '', value: true }, { label: '', value: false });
    } else if (t === 'integer' || t === 'number') {
        const bounded = schema.minimum !== undefined && schema.maximum !== undefined;
        if (bounded) {
            samples.push({ label: 'min', value: scalarSample(schema, 'low') });
            samples.push({ label: 'mid', value: scalarSample(schema, 'mid') });
            samples.push({ label: 'max', value: scalarSample(schema, 'high') });
        } else {
            samples.push({ label: '', value: scalarSample(schema, 'low') });
            samples.push({ label: '', value: scalarSample(schema, 'high') });
        }
    } else if (schema.format === 'date-time') {
        samples.push({ label: 'now', value: scalarSample(schema, 'mid') });
        samples.push({ label: '3 h ago', value: scalarSample(schema, 'low') });
    } else if (schema.format === 'duration') {
        samples.push({ label: '', value: scalarSample(schema, 'high') });
        samples.push({ label: '', value: scalarSample(schema, 'low') });
    } else {
        samples.push({ label: '', value: scalarSample(schema, 'mid') });
    }

    if (isNullable(schema)) samples.push({ label: 'null', value: null });
    return samples;
}

// The attribute-payoff summary for one item: which presentation/schema metadata the author
// wrote (chips), and the high-value omissions for this item's shape (hints). The missing list
// is deliberately selective so the block-level gap count discriminates: only metadata whose
// absence visibly degrades THIS item's value rendering — unit on numerics, labels on enums,
// format on temporals, bounds on writable numerics. Description and decimals are chips when
// authored but never gaps: they have sane defaults, and nagging them on every row saturates
// the metric into noise.
export function presentationFacts(item) {
    const schema = item.schema || {};
    const p = item.presentation || {};
    const t = effectiveType(schema);
    const numeric = t === 'integer' || t === 'number';
    const temporal = schema.format === 'date-time' || schema.format === 'duration';
    const authored = [];
    const missing = [];

    const authoredTitle = resolveAuthoredTitle(item);
    if (authoredTitle) authored.push(`name “${authoredTitle}”`);
    if (p.group) authored.push(`group ${p.group}`);
    if (p.importance) authored.push(p.importance.toLowerCase());
    if (p.uiHint) authored.push(`uiHint ${p.uiHint}`);
    if (p.order !== undefined && p.order !== null) authored.push(`order ${p.order}`);
    if (p.decimals !== undefined && p.decimals !== null) authored.push(`${p.decimals} dp`);
    if (schema['x-unit']) authored.push(`unit ${schema['x-unit']}`);
    else if (numeric) missing.push('unit');
    if (schema.minimum !== undefined || schema.maximum !== undefined) authored.push(`bounds ${schema.minimum ?? '−∞'}–${schema.maximum ?? '∞'}`);
    // Bounds only pay off on inputs (validation, slider ranges) — don't nag read-only metrics.
    else if (numeric && item._kind === 'property' && isWritable(item)) missing.push('bounds');
    if (p.format) authored.push(`format ${p.format}`);
    else if (temporal) missing.push('format');
    const labelCount = p.enumLabels ? Object.keys(p.enumLabels).length : 0;
    if (labelCount) authored.push(`${labelCount} enum labels`);
    else if (enumMembers(schema)) missing.push('enum labels');
    const mappingCount = p.statusMappings ? Object.keys(p.statusMappings).length : 0;
    if (mappingCount) authored.push(`${mappingCount} status mappings`);
    if (schema.description) authored.push('description');

    return { authored, missing };
}

// ── Scenario / Player policy (RFC 0006) ─────────────────────────────────────────

// Parse a scenario name path: Block.Property or Block.Service.Property → { block, service, property }
// (service null in the two-segment form). Returns null when the shape is wrong.
export function parseNamePath(path) {
    const segments = String(path || '').split('.');
    if (segments.some(s => !s)) return null;
    if (segments.length === 2) return { block: segments[0], service: null, property: segments[1] };
    if (segments.length === 3) return { block: segments[0], service: segments[1], property: segments[2] };
    return null;
}

// Step status → glyph for the Player's step list.
export const STEP_GLYPHS = { pending: '◌', running: '▸', ok: '✓', failed: '✗', skipped: '⊘' };

// Render a comparand: a { path } object → "{Block.Prop}", everything else → JSON literal.
// Mirrors ScenarioRunner.DescribeComparand (the {path} relational form is expect-only per the schema
// but the helper is shared to keep describeComparator uniform).
function describeComparand(v) {
    if (v !== null && typeof v === 'object' && 'path' in v && typeof v.path === 'string') {
        return `{${v.path}}`;
    }
    return JSON.stringify(v);
}

// Human-readable comparator — mirrors ScenarioRunner.DescribeComparator (C#).
// Covers above / below / equals (+tolerance) / notEquals / oneOf, and {path} comparands.
function describeComparator(c) {
    if (c.above !== undefined) return `> ${describeComparand(c.above)}`;
    if (c.below !== undefined) return `< ${describeComparand(c.below)}`;
    if ('equals' in c) {
        const tol = c.tolerance !== undefined ? ` ±${c.tolerance}` : '';
        return `== ${describeComparand(c.equals)}${tol}`;
    }
    if (c.notEquals !== undefined) return `!= ${describeComparand(c.notEquals)}`;
    // oneOf: render as "one of [a, b, c]"
    const members = Array.isArray(c.oneOf) ? c.oneOf.map(e => JSON.stringify(e)).join(', ') : '?';
    return `one of [${members}]`;
}

// Human-readable waitUntil condition — the same "> 0 · 30 s timeout" wording the server puts in
// run reports, used for pending (pre-run) rows derived from the file.
export function describeWaitUntil(waitUntil, timeoutSeconds) {
    return `${describeComparator(waitUntil)} · ${timeoutSeconds ?? 20} s timeout`;
}

// Human-readable expect assertion — mirrors ScenarioRunner.DescribeExpect (C#).
// Used for pending (pre-run) rows; the server report overwrites these once the run completes.
export function describeExpect(expect) {
    return describeComparator(expect);
}

// Human-readable serviceProviderExpect / output assertion comparator (above/below/equals(+tolerance)/notEquals/oneOf).
export function describeOutputAssert(output) {
    return describeComparator(output);
}

// The short badge for a wired service-provider contract in the wiring panel. The four built-in HAL
// families map to their familiar DI/DO/AI/AO; every other [ServiceProviderContractType] value contract
// (PPC and the like) is a generic 'SP' — NOT silently bucketed into 'AO' (the old bug that mislabeled
// custom contracts and faked a 0 read-out). SP contracts are scenario-driven; the panel shows that
// honestly rather than offering a control that cannot drive their (possibly struct) payload.
export function contractTypeShort(type) {
    return { DigitalInput: 'DI', DigitalOutput: 'DO', AnalogInput: 'AI', AnalogOutput: 'AO' }[type] || 'SP';
}

// Build the copy-paste verification report (markdown) from the scenario, the run report, and the
// human's judgment ticks ('ok' | 'notOk' keyed `${runId}/${index}`). What lands in the PR.
export function buildVerificationReport(scenario, run, judgeTicks) {
    const lines = [];
    lines.push(`## Scenario verification — \`${run.scenarioId}\``);
    if (run.title) lines.push(`**${run.title}**`);
    lines.push('');
    lines.push(`- topology: \`${run.topology}\` · run \`${run.runId}\` · status **${run.status}**`);
    if (run.fileHash) lines.push(`- file \`${run.fileHash}\` (git blob hash)`);
    lines.push(`- started ${run.startedAt}${run.elapsedSeconds ? ` · ${run.elapsedSeconds.toFixed(1)} s` : ''}`);
    const specs = (scenario && scenario.specs) || [];
    if (specs.length) lines.push(`- specs: ${specs.join(', ')}`);
    lines.push('');

    const section = (title, steps) => {
        if (!steps || !steps.length) return;
        lines.push(`### ${title}`);
        steps.forEach(s => {
            const glyph = STEP_GLYPHS[s.status] || s.status;
            const argument = s.argument ? ` \`${s.argument}\`` : '';
            const elapsed = s.elapsedMs === null || s.elapsedMs === undefined ? '' : ` (${Math.round(s.elapsedMs)} ms)`;
            const spec = s.spec ? ` — ${s.spec}` : '';
            const detail = s.detail ? ` — ${s.detail}` : '';
            lines.push(`- ${glyph} ${s.kind} \`${s.target}\`${argument}${s.label ? ` — ${s.label}` : ''}${elapsed}${spec}${detail}`);
        });
        lines.push('');
    };
    if (run.validationErrors && run.validationErrors.length) {
        lines.push('### Validation');
        run.validationErrors.forEach(e => lines.push(`- ✗ ${e}`));
        lines.push('');
    }
    section('Setup', run.setup);
    section('Steps', run.steps);

    if (run.judge && run.judge.length) {
        lines.push('### Judgments');
        run.judge.forEach((j, i) => {
            const tick = judgeTicks[`${run.runId}/${i}`];
            const box = tick === 'ok' ? '[x]' : tick === 'notOk' ? '[✗]' : '[ ]';
            lines.push(`- ${box} ${j.text}${j.spec ? ` — ${j.spec}` : ''}${tick ? '' : ' (not judged)'}`);
        });
        lines.push('');
    }
    return lines.join('\n');
}

// Platform default group order + labels (well-known keys; integrator keys render verbatim).
export const PLATFORM_DEFAULT_GROUP_ORDER = ['alarm', 'status', 'metric', 'configuration', 'diagnostics', 'identity', ''];

export const GROUP_LABELS = {
    '': 'ungrouped',
    'alarm': 'alarm',
    'status': 'status',
    'metric': 'metric',
    'configuration': 'configuration',
    'diagnostics': 'diagnostics',
    'identity': 'identity',
};

export function cssGroupKey(g) {
    if (!g) return 'ungrouped';
    return g.toLowerCase().replace(/[^a-z0-9-]/g, '-');
}

// The collapse-by-default policy (per the dashboard's proven defaults, see design package):
// a group renders open when it is one of {alarm, status, configuration, ungrouped} AND has
// twelve or fewer items; everything else starts collapsed behind its count.
export function defaultOpen(groupKey, itemCount) {
    return ['alarm', 'status', 'configuration', ''].includes(groupKey) && itemCount <= 12;
}

// Group render order: block-level [LogicBlock(Groups)] wins; remaining well-known groups follow
// platform order; unknown integrator keys sort alphabetically last.
export function orderedGroupKeys(blockGroups, itemsByGroup) {
    const seen = new Set();
    const ordered = [];
    (blockGroups || []).forEach(g => { if (itemsByGroup[g] && !seen.has(g)) { ordered.push(g); seen.add(g); } });
    PLATFORM_DEFAULT_GROUP_ORDER.forEach(g => { if (itemsByGroup[g] && !seen.has(g)) { ordered.push(g); seen.add(g); } });
    Object.keys(itemsByGroup).sort().forEach(g => { if (!seen.has(g)) { ordered.push(g); seen.add(g); } });
    return ordered;
}

// Split a service's properties + measuring points into presentation groups, sorted within each
// group by explicit Order then natural introspection position.
// The renderable members of a service: its properties plus measuring points, with a member that is BOTH
// (the cross-fill pattern: a property that is also a logged metric) collapsed to a SINGLE entry — the
// writable property row wins, since it carries the editable control AND the live value, and is flagged
// _alsoMetric. Only measuring points with no matching property become their own read-out rows. This is the
// one source of truth for "what members does this service surface", so every view (Explorer rows, gallery,
// the primary strip, the palette, counts, filter) dedupes identically. Mirrors the dashboard's
// measuringPointMerge policy; keyed by identifier within the one service.
export function serviceMembers(service) {
    const members = [];
    const propertyIds = new Set();
    (service.serviceProperties || []).forEach(p => {
        propertyIds.add(p.identifier);
        members.push({ ...p, _kind: 'property' });
    });
    (service.serviceMeasuringPoints || []).forEach(p => {
        if (propertyIds.has(p.identifier)) {
            const property = members.find(it => it._kind === 'property' && it.identifier === p.identifier);
            if (property) property._alsoMetric = true;
            return;
        }
        members.push({ ...p, _kind: 'measuringPoint' });
    });
    return members;
}

export function groupItems(service) {
    const items = serviceMembers(service);

    const itemsByGroup = {};
    items.forEach((it, naturalIdx) => {
        const groupKey = (it.presentation && it.presentation.group) || '';
        if (!itemsByGroup[groupKey]) itemsByGroup[groupKey] = [];
        itemsByGroup[groupKey].push({ item: it, natural: naturalIdx });
    });

    Object.keys(itemsByGroup).forEach(key => {
        itemsByGroup[key].sort((a, b) => {
            const ao = a.item.presentation && a.item.presentation.order;
            const bo = b.item.presentation && b.item.presentation.order;
            const aHas = ao !== undefined && ao !== null;
            const bHas = bo !== undefined && bo !== null;
            if (aHas && bHas) return ao - bo;
            if (aHas) return ao - b.natural;
            if (bHas) return a.natural - bo;
            return a.natural - b.natural;
        });
        itemsByGroup[key] = itemsByGroup[key].map(e => e.item);
    });

    return itemsByGroup;
}
