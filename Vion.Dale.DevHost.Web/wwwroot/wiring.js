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
