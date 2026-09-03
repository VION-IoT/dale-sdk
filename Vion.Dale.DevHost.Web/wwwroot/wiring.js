// wwwroot/wiring.js — pure, DOM-free, store-free. Client-side topology wiring logic (RFC 0013 Phase 2).
// Mirrors the dashboard's frozen LinkMultiplicity contract (Vion.Dale.Sdk.Core.LinkMultiplicity).

import { compilePredicate } from './predicates.js';

export const Multiplicity = { ExactlyOne: 'ExactlyOne', ZeroOrOne: 'ZeroOrOne', OneOrMore: 'OneOrMore', ZeroOrMore: 'ZeroOrMore' };
export const isRequired = m => m === Multiplicity.ExactlyOne || m === Multiplicity.OneOrMore;
export const allowsMultiple = m => m === Multiplicity.OneOrMore || m === Multiplicity.ZeroOrMore;

export function defByType(definitions, typeFullName) {
    return (definitions || []).find(d => d.typeFullName === typeFullName) || null;
}
export function interfacesOf(definitions, instance) {
    const def = defByType(definitions, instance.typeFullName);
    return def ? def.interfaces : [];
}
export function interfacesMatch(a, b) {
    const aMatch = a.matchingInterfaceTypeFullNames || [], bTypes = b.interfaceTypeFullNames || [];
    const bMatch = b.matchingInterfaceTypeFullNames || [], aTypes = a.interfaceTypeFullNames || [];
    return aMatch.some(m => bTypes.includes(m)) || bMatch.some(m => aTypes.includes(m));
}
export function candidatesFor(definitions, instances, sourceName, sourceInterfaceId) {
    const src = instances.find(i => i.name === sourceName); if (!src) return [];
    const srcIface = interfacesOf(definitions, src).find(i => i.identifier === sourceInterfaceId); if (!srcIface) return [];
    // A source interface the chosen parameters gate out has no live endpoint — nothing to wire from it.
    if (interfaceGatedOut(definitions, src, sourceInterfaceId)) return [];
    const out = [];
    for (const inst of instances) {
        if (inst.name === sourceName) continue;
        for (const tIface of interfacesOf(definitions, inst)) {
            // Skip a target the chosen parameters gate out — wiring to it would be a dangling link.
            if (interfaceGatedOut(definitions, inst, tIface.identifier)) continue;
            if (interfacesMatch(srcIface, tIface)) out.push({ targetName: inst.name, targetInterface: tIface.identifier });
        }
    }
    return out;
}
export function residueOf(definitions, instances, mappings) {
    const res = [];
    const degree = (name, iface) => mappings.filter(m =>
        (m.sourceLogicBlockName === name && m.sourceInterfaceIdentifier === iface) ||
        (m.targetLogicBlockName === name && m.targetInterfaceIdentifier === iface)).length;
    for (const inst of instances) {
        for (const iface of interfacesOf(definitions, inst)) {
            // A gated-out interface doesn't exist for the chosen parameters — it isn't residue to resolve.
            if (interfaceGatedOut(definitions, inst, iface.identifier)) continue;
            const d = degree(inst.name, iface.identifier);
            const cands = candidatesFor(definitions, instances, inst.name, iface.identifier);
            if (isRequired(iface.multiplicity) && d === 0)
                res.push({ blockName: inst.name, interfaceIdentifier: iface.identifier, multiplicity: iface.multiplicity, kind: 'required', candidates: cands });
            else if (!allowsMultiple(iface.multiplicity) && cands.length > 1 && d === 0)
                res.push({ blockName: inst.name, interfaceIdentifier: iface.identifier, multiplicity: iface.multiplicity, kind: 'contested', candidates: cands });
        }
    }
    return res;
}
// Continuous, client-side WIRED-but-wrong detection over the draft — what the user sees before the
// server's authoritative validate. Returns [{ mappingIndex, kind, message }]:
//   incompatible — a mapping whose two endpoints' interface descriptors do not bidirectionally match
//                  (mirrors the server's per-pair predicate, just fixed to one-source-to-many).
//   overwired    — a single-writer endpoint (multiplicity disallows multiple) referenced by >1 mapping;
//                  every offending mapping on that endpoint is flagged (the "consumer wired to two
//                  managers" case — the consumer side is single-writer).
// Pure: resolves interface descriptors by identifier via interfacesOf; guards unknown ids/blocks so an
// in-progress draft (a block just removed, a stale mapping) never throws.
export function problemsOf(definitions, instances, mappings) {
    const problems = [];
    const list = mappings || [];
    const ifaceOf = (blockName, ifaceId) => {
        const inst = (instances || []).find(i => i.name === blockName);
        if (!inst) return null;
        return interfacesOf(definitions, inst).find(i => i.identifier === ifaceId) || null;
    };

    // incompatible — both endpoints resolve and do NOT match. Unresolved endpoints are left to residue /
    // the server (a dangling name isn't an "incompatible" wire), so we only flag genuine type mismatches.
    list.forEach((m, i) => {
        const srcIface = ifaceOf(m.sourceLogicBlockName, m.sourceInterfaceIdentifier);
        const tgtIface = ifaceOf(m.targetLogicBlockName, m.targetInterfaceIdentifier);
        if (srcIface && tgtIface && !interfacesMatch(srcIface, tgtIface)) {
            problems.push({
                mappingIndex: i, kind: 'incompatible',
                message: `${m.sourceLogicBlockName}.${m.sourceInterfaceIdentifier} is not compatible with ${m.targetLogicBlockName}.${m.targetInterfaceIdentifier}`,
            });
        }
    });

    // overwired — group mappings by the endpoints they touch (both ends), then for each endpoint whose
    // multiplicity is single-writer and which >1 mapping references, flag every mapping on it.
    const endpoints = new Map();
    const touch = (blockName, ifaceId, mappingIndex) => {
        const key = `${blockName} ${ifaceId}`;
        if (!endpoints.has(key)) endpoints.set(key, { blockName, ifaceId, indices: [] });
        endpoints.get(key).indices.push(mappingIndex);
    };
    list.forEach((m, i) => {
        touch(m.sourceLogicBlockName, m.sourceInterfaceIdentifier, i);
        touch(m.targetLogicBlockName, m.targetInterfaceIdentifier, i);
    });
    for (const ep of endpoints.values()) {
        if (ep.indices.length <= 1) continue;
        const iface = ifaceOf(ep.blockName, ep.ifaceId);
        if (!iface || allowsMultiple(iface.multiplicity)) continue;
        ep.indices.forEach(i => problems.push({
            mappingIndex: i, kind: 'overwired',
            message: `${ep.blockName}.${ep.ifaceId} is single-writer but wired ${ep.indices.length} times`,
        }));
    }

    return problems;
}

// A mapping whose endpoint is gated OUT by [IncludedWhen] for the instance's chosen
// [InstantiationParameter] values is a hidden link / contract — it would not exist at runtime, so flag it at
// edit time (the server validate is gating-agnostic). Returns [{ mappingIndex?, kind:'gated-out', message }] —
// interface-mapping problems carry the mapping index (per-row accent); contract-mapping ones are footer-only.
// Fail-open: an ungated member, an unparseable predicate, or a referenced parameter with neither a chosen value
// nor a known default is never flagged (matching the DevHost's fail-open live view).
function paramContext(def, instance) {
    const chosen = (instance && instance.instantiationParameters) || {};
    const defaults = {};
    for (const p of (def && def.instantiationParameters) || []) {
        if (p.default !== undefined && p.default !== null) defaults[p.identifier] = p.default;
    }
    return name => (Object.prototype.hasOwnProperty.call(chosen, name) ? chosen[name] : defaults[name]);
}
function memberGatedOut(includedWhen, def, instance) {
    if (!includedWhen) return false; // ungated → always included
    const compiled = compilePredicate(includedWhen);
    if (!compiled.ok) return false; // fail-open on a predicate we can't parse
    const value = paramContext(def, instance);
    for (const ref of compiled.refs) {
        if (value(ref.property) === undefined) return false; // unresolved param → fail-open
    }
    try {
        return !compiled.evaluate(ref => value(ref.property));
    } catch (e) {
        return false;
    }
}
// Whether an interface binding on `instance` is gated OUT for its chosen [InstantiationParameter] values, so
// AutoConnect never proposes a wire to an endpoint the parameters removed, and residue never nags to wire one.
export function interfaceGatedOut(definitions, instance, interfaceIdentifier) {
    const def = defByType(definitions, instance.typeFullName);
    if (!def) return false;
    const iface = (def.interfaces || []).find(i => i.identifier === interfaceIdentifier);
    return iface ? memberGatedOut(iface.includedWhen, def, instance) : false;
}
// A gate whose parameter has a KNOWN null default and no chosen value fails that whole instance closed at
// runtime: Live-mode evaluation of a null throws out of Configure, so the block never starts. That is not a
// wiring problem — it is a missing value — so it is reported on its own, footer-level, and only where the
// catalog says the default was read. An UNKNOWN default stays fail-open: nothing is known, so nothing is said.
export function missingParameterValueProblems(definitions, instances) {
    const problems = [];
    for (const inst of instances || []) {
        const def = defByType(definitions, inst.typeFullName);
        if (!def) continue;
        const chosen = inst.instantiationParameters || {};
        const needed = new Set();
        for (const member of [...(def.interfaces || []), ...(def.contracts || [])]) {
            if (!member.includedWhen) continue;
            const compiled = compilePredicate(member.includedWhen);
            if (!compiled.ok) continue;
            for (const ref of compiled.refs) needed.add(ref.property);
        }
        for (const p of def.instantiationParameters || []) {
            if (!needed.has(p.identifier)) continue;
            if (!p.defaultKnown || p.default !== null) continue;
            if (Object.prototype.hasOwnProperty.call(chosen, p.identifier) && chosen[p.identifier] !== null) continue;
            problems.push({
                kind: 'missing-parameter-value',
                message: `${inst.name}.${p.identifier} has no default; supply a value — a gate reads it, and the block fails to start without one`,
            });
        }
    }
    return problems;
}
export function gatedOutMappingProblems(definitions, instances, interfaceMappings, contractMappings) {
    const problems = [];
    const instByName = name => (instances || []).find(i => i.name === name) || null;
    const check = (name, memberId, membersOf, mappingIndex, noun) => {
        const inst = instByName(name);
        const def = inst ? defByType(definitions, inst.typeFullName) : null;
        if (!inst || !def) return;
        const member = (membersOf(def) || []).find(x => x.identifier === memberId);
        if (!member || !memberGatedOut(member.includedWhen, def, inst)) return;
        const problem = { kind: 'gated-out', message: `${name}.${memberId} is gated out by the chosen parameters — this ${noun} would not exist at runtime` };
        if (mappingIndex !== undefined) problem.mappingIndex = mappingIndex;
        problems.push(problem);
    };
    // Both endpoints of an interface mapping — either can be gated out — and each contract mapping's target.
    (interfaceMappings || []).forEach((m, i) => {
        check(m.sourceLogicBlockName, m.sourceInterfaceIdentifier, d => d.interfaces, i, 'link');
        check(m.targetLogicBlockName, m.targetInterfaceIdentifier, d => d.interfaces, i, 'link');
    });
    (contractMappings || []).forEach(m => {
        check(m.logicBlockName, m.contractIdentifier, d => d.contracts, undefined, 'contract');
    });
    return problems;
}

// ── contract pairings ────────────────────────────────────────────────────────────────────
// A pairing declares two service-provider contract ENDPOINTS to be one wire, so the editor's vocabulary
// here is (block, contract), never an endpoint triple and never an interface. Which directions actually
// materialise is the host's answer (wire-type identity, §4.3) — the client has no handler wire types, so it
// checks exactly the STRUCTURAL rules the server refuses on, and leaves the type verdict to validate/save.

export function contractsOf(definitions, instance) {
    const def = defByType(definitions, instance.typeFullName);
    return def ? (def.contracts || []) : [];
}
// Whether a contract binding is gated OUT by [IncludedWhen] for the instance's chosen parameters —
// the contract twin of interfaceGatedOut. Pairing one would declare a wire onto an endpoint that never exists.
export function contractGatedOut(definitions, instance, contractIdentifier) {
    const def = defByType(definitions, instance.typeFullName);
    if (!def) return false;
    const contract = (def.contracts || []).find(c => c.identifier === contractIdentifier);
    return contract ? memberGatedOut(contract.includedWhen, def, instance) : false;
}
// Every pairable endpoint in the draft, in block order: { blockName, contractIdentifier, contractType, label }.
// Gated-out contracts are omitted, matching how candidatesFor omits gated-out interfaces.
export function contractEndpoints(definitions, instances) {
    const endpoints = [];
    for (const inst of instances || []) {
        for (const contract of contractsOf(definitions, inst)) {
            if (contractGatedOut(definitions, inst, contract.identifier)) continue;
            endpoints.push({
                blockName: inst.name,
                contractIdentifier: contract.identifier,
                contractType: contract.matchingContractType,
                label: `${inst.name}.${contract.identifier}`,
            });
        }
    }
    return endpoints;
}
const pairingKey = e => `${e.logicBlockName} ${e.contractIdentifier}`;
// Continuous, client-side checks over the draft's contractPairings — the mirror of the server's structural
// refusals (DevTopologyFile.Parse + ContractPairingResolution.Resolve), so the author sees them while typing.
// Returns [{ pairingIndex, kind, message }] with kind one of 'unknown-block' | 'unknown-contract' |
// 'self-paired' | 'duplicate' | 'gated-out'. Type identity is deliberately absent: it needs the two handlers'
// declared wire structs, which only the host knows, and validate/save reports it naming both types.
export function pairingProblemsOf(definitions, instances, pairings) {
    const problems = [];
    const list = pairings || [];
    const instByName = name => (instances || []).find(i => i.name === name) || null;
    const seen = new Map();

    const checkEndpoint = (endpoint, index, side) => {
        if (!endpoint || !endpoint.logicBlockName || !endpoint.contractIdentifier) {
            problems.push({ pairingIndex: index, kind: 'unknown-contract', message: `pairing ${index + 1} side ${side}: logicBlockName and contractIdentifier are both required` });
            return false;
        }
        const inst = instByName(endpoint.logicBlockName);
        if (!inst) {
            problems.push({ pairingIndex: index, kind: 'unknown-block', message: `${endpoint.logicBlockName} is not a block in this topology` });
            return false;
        }
        if (!contractsOf(definitions, inst).some(c => c.identifier === endpoint.contractIdentifier)) {
            problems.push({ pairingIndex: index, kind: 'unknown-contract', message: `${endpoint.logicBlockName} has no contract ${endpoint.contractIdentifier}` });
            return false;
        }
        if (contractGatedOut(definitions, inst, endpoint.contractIdentifier)) {
            problems.push({ pairingIndex: index, kind: 'gated-out', message: `${endpoint.logicBlockName}.${endpoint.contractIdentifier} is gated out by the chosen parameters — this pairing would not exist at runtime` });
        }
        return true;
    };

    list.forEach((p, i) => {
        const a = p && p.a, b = p && p.b;
        const aOk = checkEndpoint(a, i, 'a'), bOk = checkEndpoint(b, i, 'b');
        if (!aOk || !bOk) return;
        if (pairingKey(a) === pairingKey(b)) {
            problems.push({ pairingIndex: i, kind: 'self-paired', message: `both endpoints are ${a.logicBlockName}.${a.contractIdentifier} — an echo onto the same contract is a simulator block's job, not the host's` });
            return;
        }
        // Symmetric, so (a,b) and (b,a) are the same wire — declaring it twice would read as fan-out.
        const key = [pairingKey(a), pairingKey(b)].sort().join('|');
        if (seen.has(key)) {
            problems.push({ pairingIndex: i, kind: 'duplicate', message: `${a.logicBlockName}.${a.contractIdentifier} and ${b.logicBlockName}.${b.contractIdentifier} are already paired — a pairing is symmetric, so declare it once` });
            return;
        }
        seen.set(key, i);
    });

    return problems;
}

export function autoConnect(definitions, instances, mappings) {
    const next = mappings.slice();
    const has = (sn, si, tn, ti) => next.some(m =>
        (m.sourceLogicBlockName === sn && m.sourceInterfaceIdentifier === si && m.targetLogicBlockName === tn && m.targetInterfaceIdentifier === ti) ||
        (m.sourceLogicBlockName === tn && m.sourceInterfaceIdentifier === ti && m.targetLogicBlockName === sn && m.targetInterfaceIdentifier === si));
    for (const inst of instances) {
        for (const iface of interfacesOf(definitions, inst)) {
            const cands = candidatesFor(definitions, instances, inst.name, iface.identifier);
            const wireable = allowsMultiple(iface.multiplicity) ? cands : (cands.length === 1 ? cands : []);
            for (const c of wireable) {
                if (!has(inst.name, iface.identifier, c.targetName, c.targetInterface))
                    next.push({ sourceLogicBlockName: inst.name, sourceInterfaceIdentifier: iface.identifier, targetLogicBlockName: c.targetName, targetInterfaceIdentifier: c.targetInterface });
            }
        }
    }
    return next;
}
