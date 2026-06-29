// Pure scenario-step policy (the wiring.js analog for scenarios): the closed kind catalog, per-kind
// structural validation (mirrors Vion.Dale.DevHost/Scenarios/ScenarioFile.cs StructuralErrors), value
// coercion, and Block.Property / contract / struct-field enumeration from a /api/configuration payload.
// No DOM / Vue / store / fetch — node-testable (scenario-forms.test.mjs). The SERVER's StructuralErrors
// is authoritative; this is the advisory client mirror + the editor's field specs.

// The seven closed step shapes, in ScenarioStep.Kind discriminator order. Adding a kind is a four-site
// change (C# model+runner, CLI ScenarioFileChecks, JSON schema, this catalog) — do not fork the order.
export const KINDS = {
    set:                   { label: 'set',                  field: 'set',                  setupOk: true,  assert: false },
    serviceProviderSet:    { label: 'serviceProviderSet',   field: 'serviceProviderSet',   setupOk: true,  assert: false },
    serviceProviderExpect: { label: 'serviceProviderExpect', field: 'serviceProviderExpect', setupOk: false, assert: true },
    waitUntil:             { label: 'waitUntil',            field: 'waitUntil',            setupOk: false, assert: true },
    expect:                { label: 'expect',               field: 'expect',               setupOk: false, assert: true },
    advance:               { label: 'advance',              field: 'advance',              setupOk: false, assert: false },
    settle:                { label: 'settle',               field: 'settle',               setupOk: false, assert: false },
};

export const STEP_KIND_IDS = Object.keys(KINDS);
export const SETUP_KIND_IDS = STEP_KIND_IDS.filter(k => KINDS[k].setupOk);

// Which closed shape a step object is — exactly the C# ScenarioStep.Kind logic (first non-null field wins).
export function kindOf(step) {
    for (const id of STEP_KIND_IDS) {
        if (step && step[id] != null) return id;
    }
    return 'unknown';
}

// ---------------------------------------------------------------------------
// Config enumeration — Block.Property paths, struct fields, contract refs
// ---------------------------------------------------------------------------

// True when a service member is writable (mirrors format.js isWritable: schema.readOnly === true → read-only).
function writable(item) { return !(item && item.schema && item.schema.readOnly === true); }

// Block.Property paths from a config, de-duplicated by name (a dual-annotated member — both a
// [ServiceProperty] and a [ServiceMeasuringPoint] — appears once; the server gates them as two streams
// server-side, but the editor offers one path). writableOnly excludes read-only props + all measuring
// points (for `set`); otherwise every observable member is offered (for expect / waitUntil / watch).
//
// Wire shape (confirmed against ConfigurationOutput.cs + components.js):
//   config.logicBlocks[].name, .services[].serviceProperties[], .services[].serviceMeasuringPoints[]
//   member.identifier, member.schema.readOnly
export function propertyPaths(config, { writableOnly } = {}) {
    const seen = new Set();
    const out = [];
    for (const lb of (config && config.logicBlocks) || []) {
        for (const svc of lb.services || []) {
            const members = [
                ...(svc.serviceProperties || []),
                ...(writableOnly ? [] : (svc.serviceMeasuringPoints || [])),
            ];
            for (const m of members) {
                if (writableOnly && !writable(m)) continue;
                const path = `${lb.name}.${m.identifier}`;
                if (seen.has(path)) continue;
                seen.add(path);
                out.push(path);
            }
        }
    }
    return out;
}

// The struct field paths (Block.Property.Field) for a struct-typed member, or [] if it isn't a struct.
// The runner's ScenarioResolver already resolves these (ResolvedProperty.FieldPath).
export function structFieldPaths(config, path) {
    const member = findMember(config, path);
    const props = member && member.schema && member.schema.properties;
    return props ? Object.keys(props).map(f => `${path}.${f}`) : [];
}

// {logicBlock, contract} for every LogicBlockContract entry (lb.contracts[]).
// Wire shape confirmed: contracts are on the LogicBlock (not Service); field is `matchingContractType`.
export function contractRefs(config) {
    const out = [];
    for (const lb of (config && config.logicBlocks) || []) {
        for (const c of lb.contracts || []) out.push({ logicBlock: lb.name, contract: c.identifier });
    }
    return out;
}

// Resolve "Block.Property" (two-segment) to its member object (for schema / struct lookups). Ambiguous
// Block.Service.Property is out of scope for v1 enumeration (the picker emits the 2-seg form).
function findMember(config, path) {
    const [block, prop] = String(path).split('.');
    for (const lb of (config && config.logicBlocks) || []) {
        if (lb.name !== block) continue;
        for (const svc of lb.services || []) {
            const m = [...(svc.serviceProperties || []), ...(svc.serviceMeasuringPoints || [])].find(x => x.identifier === prop);
            if (m) return m;
        }
    }
    return null;
}
export { findMember };

// ---------------------------------------------------------------------------
// Advisory mirror of ScenarioStep.StructuralErrors(setupOnlyShapes). The SERVER is authoritative (PUT
// Save re-runs the real thing); this is for inline editor feedback. `hasValue` distinguishes an explicit
// value (incl. null) from an absent one — the editor passes value !== undefined.
export function stepErrors(step, setupOnly) {
    const errors = [];
    const shapes = STEP_KIND_IDS.filter(id => step && step[id] != null);
    if (shapes.length !== 1) {
        errors.push('a step is exactly one of ' + STEP_KIND_IDS.join(' / '));
        return errors;
    }
    const kind = shapes[0];
    if (setupOnly && !KINDS[kind].setupOk) {
        errors.push('setup entries stage state (set / serviceProviderSet) — waits, expects, output asserts, and time steps belong in steps');
        return errors;
    }
    if (kind === 'set') {
        if (!String(step.set).trim()) errors.push('set: empty name path');
        if (step.value === undefined) errors.push('set requires value (use an explicit null to write null)');
    }
    if (kind === 'serviceProviderSet') {
        const r = step.serviceProviderSet || {};
        if (!r.logicBlock || !r.contract) errors.push('serviceProviderSet: logicBlock and contract are required');
        if (step.value === undefined) errors.push('serviceProviderSet requires value');
    }
    if (kind === 'expect' || kind === 'waitUntil') {
        const a = step[kind] || {};
        if (!String(a.property || '').trim()) errors.push(kind + ': property is required');
    }
    if (kind === 'serviceProviderExpect') {
        const a = step.serviceProviderExpect || {};
        if (!a.logicBlock || !a.contract) errors.push('serviceProviderExpect: logicBlock and contract are required');
    }
    return errors;
}

// Whole-draft errors (topology required; per-section step errors; empty watch / judge entries) — mirrors
// ScenarioFile.StructuralErrors. Returns a flat list of `section[i]: message` strings.
export function scenarioErrors(draft) {
    const errors = [];
    if (!draft) return ['scenario is empty'];
    if (!String(draft.topology || '').trim()) errors.push('topology is required');
    (draft.setup || []).forEach((s, i) => stepErrors(s, true).forEach(e => errors.push(`setup[${i}]: ${e}`)));
    (draft.steps || []).forEach((s, i) => stepErrors(s, false).forEach(e => errors.push(`steps[${i}]: ${e}`)));
    (draft.watch || []).forEach((w, i) => { if (!String(w || '').trim()) errors.push(`watch[${i}]: empty name path`); });
    (draft.judge || []).forEach((j, i) => { if (!String((j && j.text) || '').trim()) errors.push(`judge[${i}]: text is required`); });
    return errors;
}
