import assert from 'node:assert/strict';
import { KINDS, kindOf, STEP_KIND_IDS, SETUP_KIND_IDS } from './wwwroot/scenario-forms.js';
import { stepErrors } from './wwwroot/scenario-forms.js';
import { propertyPaths, contractRefs, structFieldPaths } from './wwwroot/scenario-forms.js';
import { valueEditorFor, contractValueEditor } from './wwwroot/scenario-forms.js';

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

// exactly-one-shape (StructuralErrors: shapes != 1)
assert.ok(stepErrors({}, false).some(e => /exactly one of/.test(e)));
assert.ok(stepErrors({ set: 'A.B', value: 1, advance: { seconds: 1 } }, false).some(e => /exactly one of/.test(e)));
// setup is drive-only (setupOnlyShapes)
assert.ok(stepErrors({ expect: { property: 'A.B', equals: 1 } }, true).some(e => /setup entries stage state/.test(e)));
assert.deepEqual(stepErrors({ set: 'A.B', value: 1 }, true), []);
// set requires a value (Value.ValueKind == Undefined)
assert.ok(stepErrors({ set: 'A.B' }, false).some(e => /set requires value/.test(e)));
// empty name path
assert.ok(stepErrors({ set: '   ', value: 1 }, false).some(e => /empty name path/.test(e)));
// a valid expect passes
assert.deepEqual(stepErrors({ expect: { property: 'A.B', equals: 1 } }, false), []);
console.log('task2 ok');

// Wire shape confirmed against ConfigurationOutput.cs + components.js:
//   - lb.contracts (LogicBlockContract) is on the LogicBlock, NOT inside a Service
//   - contract wire field is matchingContractType (not contractType)
//   - serviceProperties / serviceMeasuringPoints / schema.readOnly all confirmed
const cfg = { logicBlocks: [ { name: 'Grid', services: [ {
    serviceProperties: [
        { identifier: 'Setpoint', schema: { readOnly: false } },
        { identifier: 'Reading',  schema: { readOnly: true } },
        { identifier: 'Phases',   schema: { readOnly: false, type: 'object', properties: { l1: {}, l2: {}, l3: {} } } },
    ],
    serviceMeasuringPoints: [ { identifier: 'Reading', schema: { readOnly: true } } ], // dual-annotated w/ the property
} ], contracts: [ { identifier: 'Demand', matchingContractType: 'GridDemand' } ] } ] };
// writable-only for set, de-duped (Reading appears once across prop+measuring, excluded as read-only)
assert.deepEqual(propertyPaths(cfg, { writableOnly: true }), ['Grid.Setpoint', 'Grid.Phases']);
// all observable for expect/watch, Reading de-duped to a single entry
assert.deepEqual(propertyPaths(cfg, { writableOnly: false }), ['Grid.Setpoint', 'Grid.Reading', 'Grid.Phases']);
// struct fields drill: Block.Property.Field
assert.deepEqual(structFieldPaths(cfg, 'Grid.Phases'), ['Grid.Phases.l1', 'Grid.Phases.l2', 'Grid.Phases.l3']);
// contracts -> {logicBlock, contract}
assert.deepEqual(contractRefs(cfg), [{ logicBlock: 'Grid', contract: 'Demand' }]);
console.log('task3 ok');

// property value editor is schema-driven
assert.equal(valueEditorFor({ readOnly: false, enum: ['A', 'B'] }).control, 'enum');
assert.equal(valueEditorFor({ type: 'boolean' }).control, 'bool');
assert.equal(valueEditorFor({ type: 'number' }).control, 'number');
assert.equal(valueEditorFor({ type: 'object', properties: { l1: {} } }).control, 'struct');
assert.equal(valueEditorFor({ type: 'array', items: {} }).control, 'array');
// nullable members carry the union type form (#105) — still classify (not rawJson)
assert.equal(valueEditorFor({ type: ['object', 'null'], properties: { l1: {} } }).control, 'struct');
assert.equal(valueEditorFor({ type: ['number', 'null'] }).control, 'number');
// contract values: scalar families form-drive by convention; non-scalar -> raw JSON (Q1 UI-only stopgap)
assert.equal(contractValueEditor('DigitalInput').control, 'bool');
assert.equal(contractValueEditor('AnalogInput').control, 'number');
assert.equal(contractValueEditor('GridDemand').control, 'rawJson'); // no value schema → fallback
console.log('task4 ok');
