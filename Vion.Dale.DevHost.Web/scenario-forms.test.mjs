import assert from 'node:assert/strict';
import { KINDS, kindOf, STEP_KIND_IDS, SETUP_KIND_IDS } from './wwwroot/scenario-forms.js';

// The seven closed shapes, the four-vocabulary-sites source of truth (ScenarioStep.Kind).
assert.deepEqual(STEP_KIND_IDS, ['set', 'serviceProviderSet', 'serviceProviderExpect', 'waitUntil', 'expect', 'advance', 'settle']);
// setup is drive-only (StructuralErrors setupOnlyShapes).
assert.deepEqual(SETUP_KIND_IDS, ['set', 'serviceProviderSet']);
// Each kind has a label + a discriminator field name.
assert.equal(KINDS.set.label, 'set');
assert.equal(KINDS.set.field, 'set');
assert.equal(KINDS.serviceProviderExpect.field, 'serviceProviderExpect');
// kindOf reads the discriminator off a step object (mirrors ScenarioStep.Kind).
assert.equal(kindOf({ set: 'A.B', value: 1 }), 'set');
assert.equal(kindOf({ advance: { seconds: 5 } }), 'advance');
assert.equal(kindOf({ label: 'x' }), 'unknown');
console.log('task1 ok');
