// A gate whose parameter has a known null default and no chosen value is a missing value, not a wiring
// problem: the block fails closed at Configure. The catalog's defaultKnown flag is what lets the editor tell
// that apart from a default it simply could not read.

import { test } from 'node:test';
import assert from 'node:assert/strict';

import { missingParameterValueProblems } from '../wwwroot/wiring.js';

const definitionWith = parameter => [
    {
        typeFullName: 'Fixture.Station',
        interfaces: [{ identifier: 'Point2_ISink', interfaceTypeFullNames: [], matchingInterfaceTypeFullNames: [], multiplicity: 'ZeroOrOne', includedWhen: 'Reserve >= 1' }],
        contracts: [],
        instantiationParameters: [parameter],
    },
];
const station = chosen => ({ name: 'Station', typeFullName: 'Fixture.Station', instantiationParameters: chosen });

test('a known null default with no chosen value is reported', () => {
    const problems = missingParameterValueProblems(definitionWith({ identifier: 'Reserve', default: null, defaultKnown: true }), [station({})]);
    assert.equal(problems.length, 1);
    assert.match(problems[0].message, /Station\.Reserve has no default/);
});

test('an unknown default stays fail-open', () => {
    const problems = missingParameterValueProblems(definitionWith({ identifier: 'Reserve', default: null, defaultKnown: false }), [station({})]);
    assert.deepEqual(problems, []);
});

test('a chosen value clears it', () => {
    const problems = missingParameterValueProblems(definitionWith({ identifier: 'Reserve', default: null, defaultKnown: true }), [station({ Reserve: 2 })]);
    assert.deepEqual(problems, []);
});

test('a parameter no gate reads is not reported', () => {
    const definitions = definitionWith({ identifier: 'Unused', default: null, defaultKnown: true });
    const problems = missingParameterValueProblems(definitions, [station({})]);
    assert.deepEqual(problems, []);
});
