// Presentation-predicate evaluator for the DevHost SPA — the UI profile of the VisibleWhen dialect
// (RFC 0017). No-build plain JS (see CLAUDE.md): a small recursive-descent parser + evaluator that
// mirrors the dale-sdk analyzer's parser and the dashboard's jsep-subset compiler, pinned by the
// shared conformance vector (Predicates/predicate-conformance.json in vion-contracts; vendored here
// as predicate-conformance.json). The analyzer parses/type-checks; this evaluates. UI profile:
// fail-open, and `null` participates as a real value (§2.3).

// ── Tokenizer ────────────────────────────────────────────────────────────────────

function tokenize(text) {
    const tokens = [];
    let i = 0;
    const isIdentStart = (c) => /[A-Za-z_]/.test(c);
    const isIdentPart = (c) => /[A-Za-z0-9_]/.test(c);
    while (i < text.length) {
        const c = text[i];
        if (/\s/.test(c)) { i++; continue; }
        if (isIdentStart(c)) {
            let j = i + 1;
            while (j < text.length && isIdentPart(text[j])) j++;
            tokens.push({ k: 'ident', v: text.slice(i, j) });
            i = j;
            continue;
        }
        if (/[0-9]/.test(c)) {
            let j = i + 1;
            while (j < text.length && /[0-9]/.test(text[j])) j++;
            const digits = text.slice(i, j);
            const value = Number(digits);
            if (!Number.isSafeInteger(value) || value > 2147483647) throw new Error(`integer literal '${digits}' is out of the supported int32 range`);
            tokens.push({ k: 'int', v: value });
            i = j;
            continue;
        }
        if (c === '\'' || c === '"') {
            let j = i + 1;
            let s = '';
            while (j < text.length) {
                const ch = text[j];
                if (ch === '\\') {
                    if (text[j + 1] === c) { s += c; j += 2; continue; }
                    throw new Error("string escapes beyond \\' (or \\\") are not in the dialect");
                }
                if (ch === c) { j++; break; }
                if (j === text.length - 1) throw new Error('unterminated string literal');
                s += ch;
                j++;
            }
            if (j > text.length) throw new Error('unterminated string literal');
            tokens.push({ k: 'str', v: s });
            i = j;
            continue;
        }
        const two = text.slice(i, i + 2);
        if (two === '==' || two === '!=' || two === '<=' || two === '>=' || two === '&&' || two === '||') {
            tokens.push({ k: two });
            i += 2;
            continue;
        }
        if (c === '<' || c === '>' || c === '!' || c === '(' || c === ')' || c === '[' || c === ']' || c === ',' || c === '.') {
            tokens.push({ k: c });
            i++;
            continue;
        }
        throw new Error(`unexpected character '${c}' (arithmetic, ternary, and function calls are not in the dialect)`);
    }
    tokens.push({ k: 'end' });
    return tokens;
}

// ── Recursive-descent parser (grammar mirrors docs/predicates.md §2.2) ─────────────

function parse(text) {
    const tokens = tokenize(text);
    let pos = 0;
    const cur = () => tokens[pos];
    const eat = (k) => { if (cur().k !== k) throw new Error(`expected '${k}' but found '${cur().v ?? cur().k}'`); pos++; };

    function parseOr() {
        let left = parseAnd();
        while (cur().k === '||') { pos++; left = { t: 'or', l: left, r: parseAnd() }; }
        return left;
    }
    function parseAnd() {
        let left = parseUnary();
        while (cur().k === '&&') { pos++; left = { t: 'and', l: left, r: parseUnary() }; }
        return left;
    }
    function parseUnary() {
        if (cur().k === '!') { pos++; return { t: 'not', o: parseNegand() }; }
        if (cur().k === '(') return parseParen();
        return parseComparisonOrRef();
    }
    // negand := boolRef | "(" predicate ")"  — NOT a comparison, so "!A == 5" is rejected.
    function parseNegand() {
        if (cur().k === '(') return parseParen();
        return { t: 'boolref', ref: parseRef() };
    }
    function parseParen() {
        eat('(');
        const inner = parseOr();
        eat(')');
        return inner;
    }
    function parseComparisonOrRef() {
        const ref = parseRef();
        const k = cur().k;
        if (k === '==' || k === '!=' || k === '<' || k === '<=' || k === '>' || k === '>=') {
            pos++;
            return { t: 'cmp', ref, op: k, lit: parseLiteral() };
        }
        if (k === 'ident' && cur().v === 'in') {
            pos++;
            eat('[');
            const items = [parseLiteral()];
            while (cur().k === ',') { pos++; items.push(parseLiteral()); }
            eat(']');
            return { t: 'in', ref, items };
        }
        return { t: 'boolref', ref };
    }
    function parseRef() {
        if (cur().k !== 'ident') throw new Error(`expected an identifier but found '${cur().v ?? cur().k}' (references sit on the left of a comparison)`);
        const first = cur().v;
        pos++;
        if (cur().k !== '.') return { service: null, property: first };
        pos++;
        if (cur().k !== 'ident') throw new Error('expected a property name after the service qualifier');
        const second = cur().v;
        pos++;
        if (cur().k === '.') throw new Error('references may have at most two segments (Property or Service.Property)');
        return { service: first, property: second };
    }
    function parseLiteral() {
        const tok = cur();
        if (tok.k === 'int') { pos++; return { t: 'int', v: tok.v }; }
        if (tok.k === 'str') { pos++; return { t: 'str', v: tok.v }; }
        if (tok.k === 'ident' && (tok.v === 'true' || tok.v === 'false')) { pos++; return { t: 'bool', v: tok.v === 'true' }; }
        if (tok.k === 'ident') throw new Error(`'${tok.v}' is not a literal — the right side of a comparison must be a literal (quote enum/string values, e.g. 'Eco')`);
        throw new Error('expected a literal (integer, true/false, or a quoted string)');
    }

    const ast = parseOr();
    if (cur().k !== 'end') throw new Error(`unexpected '${cur().v ?? cur().k}' after a complete predicate`);
    return ast;
}

function collectRefs(node, out) {
    switch (node.t) {
        case 'or':
        case 'and':
            collectRefs(node.l, out);
            collectRefs(node.r, out);
            break;
        case 'not':
            collectRefs(node.o, out);
            break;
        case 'boolref':
        case 'cmp':
        case 'in':
            out.push(node.ref);
            break;
    }
    return out;
}

// Sentinels thrown by the resolver so the fail-open ladder can distinguish an unknown ref (warn) from
// an absent value (transient, no warn).
const UNKNOWN = Symbol('unknown-ref');
const ABSENT = Symbol('absent-value');

function evalNode(node, resolve) {
    switch (node.t) {
        case 'or': return evalNode(node.l, resolve) || evalNode(node.r, resolve);
        case 'and': return evalNode(node.l, resolve) && evalNode(node.r, resolve);
        case 'not': return !evalNode(node.o, resolve);
        case 'boolref': return resolve(node.ref);
        case 'in': {
            const v = resolve(node.ref);
            return node.items.map(litValue).includes(v);
        }
        case 'cmp': {
            const v = resolve(node.ref);
            const lit = litValue(node.lit);
            switch (node.op) {
                // Loose equality is the reference behavior (the analyzer guarantees same-typed operands);
                // the only observable effect is around null, which the conformance vector pins.
                case '==': return v == lit; // eslint-disable-line eqeqeq
                case '!=': return v != lit; // eslint-disable-line eqeqeq
                case '<': return v < lit;
                case '<=': return v <= lit;
                case '>': return v > lit;
                case '>=': return v >= lit;
            }
            return false;
        }
    }
    return false;
}

function litValue(lit) {
    return lit.v;
}

// ── Public compile / evaluate ─────────────────────────────────────────────────────

const _compileCache = new Map();

export function compilePredicate(expr) {
    if (_compileCache.has(expr)) return _compileCache.get(expr);
    let compiled;
    try {
        const ast = parse(expr);
        const refs = collectRefs(ast, []);
        compiled = { ok: true, refs, evaluate: (resolve) => evalNode(ast, resolve) };
    } catch (e) {
        compiled = { ok: false, error: e.message };
    }
    _compileCache.set(expr, compiled);
    return compiled;
}

const _warned = new Set();

function warnOnce(predicate, message) {
    if (_warned.has(predicate)) return;
    _warned.add(predicate);
    if (typeof console !== 'undefined') console.warn(`[VisibleWhen] "${predicate}": ${message}`);
}

// Evaluate a predicate for visibility under the UI profile (docs/predicates.md §2.3):
//   1. no predicate           → visible
//   2. parse / out-of-subset  → visible + one warning
//   3. unresolvable ref       → visible + one warning
//   4. referenced value undefined (no retained message yet) → visible, no warning
//   5. otherwise evaluate, truthiness-test the result (explicit null participates).
// `resolveValue(ref)` returns the live value, or the UNKNOWN / ABSENT sentinel.
export function evaluateVisibility(predicate, resolveValue) {
    if (predicate === null || predicate === undefined || String(predicate).trim() === '') return true;

    const compiled = compilePredicate(predicate);
    if (!compiled.ok) {
        warnOnce(predicate, compiled.error);
        return true;
    }

    const resolve = (ref) => {
        const outcome = resolveValue(ref);
        if (outcome === UNKNOWN) throw UNKNOWN;
        if (outcome === ABSENT) throw ABSENT;
        return outcome;
    };

    try {
        return !!compiled.evaluate(resolve);
    } catch (e) {
        if (e === ABSENT) return true; // transient — retained value not arrived yet
        if (e === UNKNOWN) { warnOnce(predicate, 'a reference does not resolve to a known sibling property'); return true; }
        warnOnce(predicate, String(e && e.message ? e.message : e));
        return true;
    }
}

evaluateVisibility.UNKNOWN = UNKNOWN;
evaluateVisibility.ABSENT = ABSENT;

// ── Conformance self-test (dev-only; asserted by devhost-smoke Tier 2) ─────────────

// Runs the vendored conformance vector: every parse case against the parser, and every eval case
// (core + "profile": "ui") against the evaluator. `values` is keyed by ref string (e.g.
// "Service.Property" or "Property"); a missing key is treated as ABSENT, an explicit null as null.
export function runVectorSelfTest(vector) {
    const failures = [];

    for (const c of vector.parse || []) {
        const compiled = compilePredicate(c.predicate);
        if (compiled.ok !== c.valid) failures.push(`parse[${c.name}] "${c.predicate}": expected valid=${c.valid}, got ${compiled.ok}`);
    }

    for (const c of vector.eval || []) {
        const resolveValue = (ref) => {
            const key = ref.service === null ? ref.property : `${ref.service}.${ref.property}`;
            if (!Object.prototype.hasOwnProperty.call(c.values || {}, key)) return ABSENT;
            return c.values[key];
        };
        let visible;
        try {
            visible = evaluateVisibility(c.predicate, resolveValue);
        } catch (e) {
            failures.push(`eval[${c.name}] "${c.predicate}": threw ${e}`);
            continue;
        }
        // `expected` is the boolean the predicate evaluates to; the consumer truthiness-tests it into
        // visible/hidden, so expected === visible for these fully-resolved cases.
        if (visible !== c.expected) failures.push(`eval[${c.name}] "${c.predicate}": expected ${c.expected}, got ${visible}`);
    }

    return { passed: failures.length === 0, failures };
}

// Expose for the devhost-smoke Tier 2 harness (it fetches predicate-conformance.json and calls this).
if (typeof window !== 'undefined') {
    window.__predicates = { compilePredicate, evaluateVisibility, runVectorSelfTest };
}
