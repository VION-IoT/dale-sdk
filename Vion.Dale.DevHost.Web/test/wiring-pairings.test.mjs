// Unit tests for the contract-pairing half of wiring.js (RFC 0020) — the pure, DOM-free logic behind the
// topology editor's pairing section: which endpoints can be paired, and the client mirror of the server's
// structural refusals. Dev-time only (`node --test Vion.Dale.DevHost.Web/test/wiring-pairings.test.mjs`);
// sits above wwwroot so the embed glob never bundles it.
//
// Deliberately NOT covered here: the wire-type identity rule (RFC 0020 §4.3). It needs the two handlers'
// declared wire structs, which no client payload carries — the server reports it on validate / save, and
// ContractPairingShould pins that.

import { test } from 'node:test';
import assert from 'node:assert/strict';

import { contractEndpoints, contractGatedOut, contractsOf, pairingProblemsOf } from '../wwwroot/wiring.js';

// A two-block catalog in the /api/logic-block-definitions shape: a consumer face and its provider face,
// plus one contract gated behind an [InstantiationParameter] (RFC 0016).
const definitions = [
    {
        typeFullName: 'Fixture.IoBlock',
        interfaces: [],
        contracts: [
            { identifier: 'ActiveOutput', matchingContractType: 'DigitalOutput', includedWhen: null },
            { identifier: 'SpareOutput', matchingContractType: 'DigitalOutput', includedWhen: 'HasSpare == true' },
        ],
        instantiationParameters: [{ identifier: 'HasSpare', schema: { type: 'boolean' }, default: false }],
    },
    {
        typeFullName: 'Fixture.IdealIoBlock',
        interfaces: [],
        contracts: [{ identifier: 'OutputChannel', matchingContractType: 'DigitalOutputProvider', includedWhen: null }],
        instantiationParameters: [],
    },
];

const instances = [
    { typeFullName: 'Fixture.IoBlock', name: 'IoBlock' },
    { typeFullName: 'Fixture.IdealIoBlock', name: 'IdealIo' },
];

const endpoint = (logicBlockName, contractIdentifier) => ({ logicBlockName, contractIdentifier });
const pairing = (a, b) => ({ a, b });

test('contractsOf reads a block type\'s declared contracts', () => {
    assert.deepEqual(contractsOf(definitions, instances[1]).map(c => c.identifier), ['OutputChannel']);
    assert.deepEqual(contractsOf(definitions, { typeFullName: 'Fixture.Unknown', name: 'x' }), []);
});

test('contractEndpoints offers every pairable endpoint with its contract type, and omits gated-out ones', () => {
    const offered = contractEndpoints(definitions, instances);

    assert.deepEqual(offered.map(e => e.label), ['IoBlock.ActiveOutput', 'IdealIo.OutputChannel']);
    assert.equal(offered[1].contractType, 'DigitalOutputProvider', 'the picker shows the type so a face and its provider face are recognisable');
    assert.ok(contractGatedOut(definitions, instances[0], 'SpareOutput'), 'HasSpare defaults to false, so SpareOutput does not exist');

    // Turning the parameter on brings the endpoint back — the same fail-open gating the interface side uses.
    const withSpare = [{ ...instances[0], instantiationParameters: { HasSpare: true } }, instances[1]];
    assert.ok(contractEndpoints(definitions, withSpare).some(e => e.label === 'IoBlock.SpareOutput'));
});

test('a well-formed pairing has no client-side problems', () => {
    const problems = pairingProblemsOf(definitions, instances, [pairing(endpoint('IoBlock', 'ActiveOutput'), endpoint('IdealIo', 'OutputChannel'))]);
    assert.deepEqual(problems, []);
});

test('an endpoint naming an unknown block or contract is flagged on its row', () => {
    const problems = pairingProblemsOf(definitions, instances, [
        pairing(endpoint('Ghost', 'ActiveOutput'), endpoint('IdealIo', 'OutputChannel')),
        pairing(endpoint('IoBlock', 'NoSuchContract'), endpoint('IdealIo', 'OutputChannel')),
    ]);

    assert.deepEqual(problems.map(p => [p.pairingIndex, p.kind]), [[0, 'unknown-block'], [1, 'unknown-contract']]);
});

test('an endpoint paired with itself is refused the way the server refuses it', () => {
    const problems = pairingProblemsOf(definitions, instances, [pairing(endpoint('IoBlock', 'ActiveOutput'), endpoint('IoBlock', 'ActiveOutput'))]);

    assert.equal(problems.length, 1);
    assert.equal(problems[0].kind, 'self-paired');
    assert.match(problems[0].message, /simulator block's job/);
});

test('the same wire declared twice is flagged once, in either endpoint order', () => {
    const a = endpoint('IoBlock', 'ActiveOutput'), b = endpoint('IdealIo', 'OutputChannel');
    const problems = pairingProblemsOf(definitions, instances, [pairing(a, b), pairing(b, a)]);

    assert.deepEqual(problems.map(p => [p.pairingIndex, p.kind]), [[1, 'duplicate']], 'a pairing is symmetric, so the second declaration is the duplicate');
});

test('pairing a gated-out contract is flagged without throwing', () => {
    const problems = pairingProblemsOf(definitions, instances, [pairing(endpoint('IoBlock', 'SpareOutput'), endpoint('IdealIo', 'OutputChannel'))]);

    assert.deepEqual(problems.map(p => p.kind), ['gated-out']);
});

test('an in-progress draft never throws', () => {
    assert.deepEqual(pairingProblemsOf(definitions, instances, null), []);
    assert.equal(pairingProblemsOf(definitions, instances, [pairing(null, endpoint('IdealIo', 'OutputChannel'))])[0].kind, 'unknown-contract');
    assert.deepEqual(pairingProblemsOf(null, null, [pairing(endpoint('IoBlock', 'ActiveOutput'), endpoint('IdealIo', 'OutputChannel'))]).map(p => p.kind),
                     ['unknown-block', 'unknown-block']);
});
