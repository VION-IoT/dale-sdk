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
export function groupItems(service) {
    const items = [];
    (service.serviceProperties || []).forEach(p => items.push({ ...p, _kind: 'property' }));
    (service.serviceMeasuringPoints || []).forEach(p => items.push({ ...p, _kind: 'measuringPoint' }));

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
