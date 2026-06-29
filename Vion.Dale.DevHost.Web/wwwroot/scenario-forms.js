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
