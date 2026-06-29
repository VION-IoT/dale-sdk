// wwwroot/wiring.js — pure, DOM-free, store-free. Client-side topology wiring logic (RFC 0013 Phase 2).
// Mirrors the dashboard's frozen LinkMultiplicity contract (Vion.Dale.Sdk.Core.LinkMultiplicity).

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
    const out = [];
    for (const inst of instances) {
        if (inst.name === sourceName) continue;
        for (const tIface of interfacesOf(definitions, inst)) {
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
