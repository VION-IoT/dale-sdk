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
            // Skip a target the chosen parameters gate out — wiring to it would be a dangling link (RFC 0016).
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

// RFC 0016: a mapping whose endpoint is gated OUT by [IncludedWhen] for the instance's chosen
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
