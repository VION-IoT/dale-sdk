#!/usr/bin/env node
/**
 * generate-api-reference.cjs
 *
 * Generates markdown API reference from Dale SDK XML documentation,
 * filtered to only include types marked with [PublicApi].
 *
 * Discovers all projects automatically under --root. A project participates
 * if any of its source files contain [PublicApi]. No manual enumeration needed.
 *
 * Usage:
 *   dotnet build -c Release
 *   node scripts/generate-api-reference.cjs --root . --out api-reference.md
 *   node scripts/generate-api-reference.cjs --root . --exclude "*.Test" --out api-reference.md
 *   node scripts/generate-api-reference.cjs --root . --exclude "*.Test" --manifest docs/snapshots/publicapi-manifest.json
 */

const fs = require('fs');
const path = require('path');

// --- CLI argument parsing ---

function parseArgs() {
    const args = process.argv.slice(2);
    const result = { root: null, out: null, exclude: [], manifest: null };
    for (let i = 0; i < args.length; i++) {
        if (args[i] === '--root' && args[i + 1]) result.root = args[++i];
        else if (args[i] === '--out' && args[i + 1]) result.out = args[++i];
        else if (args[i] === '--exclude' && args[i + 1]) result.exclude.push(args[++i]);
        else if (args[i] === '--manifest' && args[i + 1]) result.manifest = args[++i];
    }
    if (!result.root) {
        console.error('Usage: node generate-api-reference.cjs --root <dir> [--out <path>] [--exclude <glob> ...] [--manifest <path>]');
        process.exit(1);
    }
    return result;
}

// --- File discovery ---

/** Recursively find files matching a predicate, skipping bin/obj/.dirs */
function findFiles(dir, predicate) {
    const results = [];
    let entries;
    try {
        entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch {
        return results;
    }
    for (const entry of entries) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            if (entry.name.startsWith('.') || entry.name === 'bin' || entry.name === 'obj' || entry.name === 'node_modules') continue;
            results.push(...findFiles(full, predicate));
        } else if (entry.isFile() && predicate(entry.name)) {
            results.push(full);
        }
    }
    return results;
}

/** Find all .csproj files under root */
function findProjects(rootDir) {
    return findFiles(rootDir, name => name.endsWith('.csproj'));
}

/** Find all .cs files under a directory */
function findCsFiles(dir) {
    return findFiles(dir, name => name.endsWith('.cs'));
}

/** Check if a project name matches any exclude pattern (simple glob with * wildcard) */
function isExcluded(projectName, excludePatterns) {
    for (const pattern of excludePatterns) {
        const re = new RegExp('^' + pattern.replace(/\./g, '\\.').replace(/\*/g, '.*') + '$');
        if (re.test(projectName)) return true;
    }
    return false;
}

// --- Project parsing ---

/** Extract assembly name from .csproj content, falling back to filename */
function getAssemblyName(csprojPath) {
    const content = fs.readFileSync(csprojPath, 'utf8');
    const match = content.match(/<AssemblyName>([^<]+)<\/AssemblyName>/);
    if (match) return match[1];
    return path.basename(csprojPath, '.csproj');
}

/** Find the XML doc file for a project after Release build */
function findXmlDoc(projectDir, assemblyName) {
    // Glob for bin/Release/**/<AssemblyName>.xml
    const binRelease = path.join(projectDir, 'bin', 'Release');
    if (!fs.existsSync(binRelease)) return null;
    const candidates = findFiles(binRelease, name => name === `${assemblyName}.xml`);
    return candidates.length > 0 ? candidates[0] : null;
}

// --- Source scanning ---

/** Extract [PublicApiNamespace("...")] values from source files */
function findPublicApiNamespaces(csFiles) {
    const namespaces = [];
    const re = /\[assembly:\s*PublicApiNamespace\(\s*"([^"]+)"\s*\)\]/g;
    for (const file of csFiles) {
        const content = fs.readFileSync(file, 'utf8');
        let match;
        while ((match = re.exec(content)) !== null) {
            namespaces.push(match[1]);
        }
    }
    return namespaces;
}

/** Extract types marked with [PublicApi] from source files.
 *  Returns a Set of fully qualified type names.
 */
function findPublicApiTypes(csFiles) {
    const types = new Set();
    const attrRe = /^\s*\[PublicApi\]/;
    const typeRe = /^\s*(?:public\s+)?(?:abstract\s+)?(?:sealed\s+)?(?:partial\s+)?(?:static\s+)?(class|interface|enum|struct|record\s+struct|record\s+class|record|extension)\s+(\w+)/;
    const nsRe = /^\s*namespace\s+([\w.]+)/;

    for (const file of csFiles) {
        const content = fs.readFileSync(file, 'utf8');
        const lines = content.split('\n');
        let currentNamespace = '';
        let nextIsPublicApi = false;
        let bracketDepth = 0;

        for (const line of lines) {
            const nsMatch = line.match(nsRe);
            if (nsMatch) {
                currentNamespace = nsMatch[1];
            }

            const trimmed = line.trim();
            const isComment = trimmed.startsWith('///') || trimmed.startsWith('//');

            // Track [...] depth so continuation lines of multi-line attributes
            // like [AttributeUsage(..., \n   AllowMultiple = true)] don't reset
            // nextIsPublicApi. Skip comments (XML doc comments may contain
            // unbalanced brackets in cref text).
            const wasInsideAttribute = bracketDepth > 0;
            if (!isComment) {
                for (const ch of line) {
                    if (ch === '[') bracketDepth++;
                    else if (ch === ']') bracketDepth = Math.max(0, bracketDepth - 1);
                }
            }

            if (attrRe.test(line)) {
                nextIsPublicApi = true;
                continue;
            }

            if (nextIsPublicApi) {
                const typeMatch = line.match(typeRe);
                if (typeMatch) {
                    const typeName = typeMatch[2];
                    const fqn = currentNamespace ? `${currentNamespace}.${typeName}` : typeName;
                    types.add(fqn);
                    nextIsPublicApi = false;
                } else if (
                    trimmed === '' ||
                    trimmed.startsWith('//') ||
                    trimmed.startsWith('[') ||
                    trimmed.startsWith('///') ||
                    wasInsideAttribute
                ) {
                    // Skip blank lines, comments, additional attributes, and
                    // continuation lines of multi-line attributes.
                } else {
                    nextIsPublicApi = false;
                }
            }
        }
    }
    return types;
}

// --- XML parsing (lightweight, no dependencies) ---

/** Parse XML doc file into a map of member name -> { summary, remarks, params, returns } */
function parseXmlDoc(xmlPath) {
    const content = fs.readFileSync(xmlPath, 'utf8');
    const members = new Map();

    const memberRe = /<member\s+name="([^"]+)">([\s\S]*?)<\/member>/g;
    let match;
    while ((match = memberRe.exec(content)) !== null) {
        const name = match[1];
        const body = match[2];
        members.set(name, {
            name,
            summary: extractTag(body, 'summary'),
            remarks: extractTag(body, 'remarks'),
            params: extractParams(body),
            returns: extractTag(body, 'returns'),
        });
    }
    return members;
}

function extractTag(xml, tag) {
    const re = new RegExp(`<${tag}>(.*?)</${tag}>`, 's');
    const match = xml.match(re);
    if (!match) return '';
    return cleanXmlText(match[1]);
}

function extractParams(xml) {
    const params = [];
    const re = /<param\s+name="([^"]+)">(.*?)<\/param>/gs;
    let match;
    while ((match = re.exec(xml)) !== null) {
        params.push({ name: match[1], description: cleanXmlText(match[2]) });
    }
    return params;
}

function cleanXmlText(text) {
    return text
        .replace(/<see\s+cref="[TPF]:([^"]+)"\s*\/>/g, (_, ref) => `\`${ref.split('.').pop()}\``)
        .replace(/<c>(.*?)<\/c>/g, '`$1`')
        .replace(/<[^>]+>/g, '')
        .replace(/\s+/g, ' ')
        .trim();
}

// --- Markdown generation ---

function generateMarkdown(assemblies) {
    const lines = [];

    // VitePress frontmatter
    lines.push('---');
    lines.push('title: SDK API Reference');
    lines.push('description: Auto-generated API reference for types marked with [PublicApi].');
    lines.push('---');
    lines.push('');
    lines.push('# Vion Dale SDK API Reference');
    lines.push('');
    lines.push('> Auto-generated from source code. Types marked with `[PublicApi]`.');
    lines.push('');

    for (const assembly of assemblies) {
        // Group types by namespace
        const grouped = new Map();
        for (const fqn of [...assembly.publicApiTypes].sort()) {
            const lastDot = fqn.lastIndexOf('.');
            const ns = lastDot > 0 ? fqn.substring(0, lastDot) : '(global)';
            if (!grouped.has(ns)) grouped.set(ns, []);
            grouped.get(ns).push(fqn);
        }

        // Order: configured namespaces first, then others
        const orderedNamespaces = [
            ...assembly.namespaces.filter(ns => grouped.has(ns)),
            ...[...grouped.keys()].filter(ns => !assembly.namespaces.includes(ns)),
        ];

        // Collapse: if single namespace matches assembly name, emit one h2 only
        const isSingleCollapsed = orderedNamespaces.length === 1 && orderedNamespaces[0] === assembly.name;

        if (!isSingleCollapsed) {
            // Emit assembly-level h2 only when there are multiple namespaces
            // or the namespace name differs from the assembly name
        }

        for (const ns of orderedNamespaces) {
            const types = grouped.get(ns);
            if (!types) continue;

            // h2 = namespace (or assembly if collapsed)
            lines.push(`## ${ns}`);
            lines.push('');

            for (const fqn of types) {
                const typeName = fqn.split('.').pop();
                const member = findXmlType(assembly.xmlMembers, fqn);
                const xmlTypePrefix = findXmlTypePrefix(assembly.xmlMembers, fqn);

                // h3 = type name (appears in VitePress outline)
                lines.push(`### ${typeName}`);
                lines.push('');
                if (member && member.summary) {
                    lines.push(member.summary);
                    lines.push('');
                }

                if (member && member.remarks) {
                    lines.push(`> ${member.remarks}`);
                    lines.push('');
                }

                // Find members (methods, properties, fields) of this type
                const memberPrefix = `${xmlTypePrefix}.`;
                const typeMembers = [];
                for (const [key, val] of assembly.xmlMembers) {
                    if ((key.startsWith('M:') || key.startsWith('P:') || key.startsWith('F:')) &&
                        key.includes(memberPrefix)) {
                        typeMembers.push({ key, ...val });
                    }
                }

                if (typeMembers.length > 0) {
                    const methods = typeMembers.filter(m => m.key.startsWith('M:'));
                    const properties = typeMembers.filter(m => m.key.startsWith('P:'));
                    const fields = typeMembers.filter(m => m.key.startsWith('F:'));

                    if (properties.length > 0) {
                        lines.push('**Properties:**');
                        lines.push('');
                        for (const prop of properties) {
                            const name = extractMemberShortName(prop.key, xmlTypePrefix);
                            lines.push(`- \`${name}\` — ${prop.summary || '*(no description)*'}`);
                        }
                        lines.push('');
                    }

                    if (methods.length > 0) {
                        lines.push('**Methods:**');
                        lines.push('');
                        for (const method of methods) {
                            const name = extractMemberShortName(method.key, xmlTypePrefix);
                            if (name.startsWith('#ctor')) {
                                lines.push(`- *Constructor* — ${method.summary || '*(no description)*'}`);
                            } else {
                                lines.push(`- \`${name}\` — ${method.summary || '*(no description)*'}`);
                            }
                            if (method.params.length > 0) {
                                for (const p of method.params) {
                                    lines.push(`  - \`${p.name}\`: ${p.description}`);
                                }
                            }
                        }
                        lines.push('');
                    }

                    if (fields.length > 0) {
                        lines.push('**Fields/Values:**');
                        lines.push('');
                        for (const field of fields) {
                            const name = extractMemberShortName(field.key, xmlTypePrefix);
                            lines.push(`- \`${name}\` — ${field.summary || '*(no description)*'}`);
                        }
                        lines.push('');
                    }
                }

                lines.push('---');
                lines.push('');
            }
        }
    }

    return lines.join('\n');
}

/** Find XML type entry for a source FQN, handling generic arity (e.g. Foo`1) */
function findXmlType(xmlMembers, fqn) {
    // Try exact match first
    const exact = xmlMembers.get(`T:${fqn}`);
    if (exact) return exact;
    // Try with generic arity suffixes `1, `2, ...
    for (let arity = 1; arity <= 4; arity++) {
        const key = `T:${fqn}\`${arity}`;
        const member = xmlMembers.get(key);
        if (member) return member;
    }
    return null;
}

/** Find the XML key prefix for a type (accounting for generic arity) */
function findXmlTypePrefix(xmlMembers, fqn) {
    if (xmlMembers.has(`T:${fqn}`)) return fqn;
    for (let arity = 1; arity <= 4; arity++) {
        const key = `${fqn}\`${arity}`;
        if (xmlMembers.has(`T:${key}`)) return key;
    }
    return fqn;
}

function extractMemberShortName(xmlKey, xmlTypePrefix) {
    const withoutPrefix = xmlKey.substring(2);
    const typePrefix = xmlTypePrefix + '.';
    let name = withoutPrefix.startsWith(typePrefix)
        ? withoutPrefix.substring(typePrefix.length)
        : withoutPrefix;
    return simplifySignature(name);
}

/** Map fully-qualified .NET type names to C# keyword equivalents */
const KEYWORD_MAP = {
    'System.Boolean': 'bool',
    'System.Byte': 'byte',
    'System.SByte': 'sbyte',
    'System.Char': 'char',
    'System.Decimal': 'decimal',
    'System.Double': 'double',
    'System.Single': 'float',
    'System.Int32': 'int',
    'System.UInt32': 'uint',
    'System.Int64': 'long',
    'System.UInt64': 'ulong',
    'System.Int16': 'short',
    'System.UInt16': 'ushort',
    'System.String': 'string',
    'System.Object': 'object',
    'System.Void': 'void',
};

/** Simplify a fully-qualified type reference to its short form */
function simplifyType(fqType) {
    // Handle array types: strip [], simplify inner, re-add
    const arrayMatch = fqType.match(/^(.+?)(\[\])$/);
    if (arrayMatch) {
        return simplifyType(arrayMatch[1]) + '[]';
    }

    // Handle Nullable{X} → X?
    const nullableMatch = fqType.match(/^System\.Nullable\{(.+)\}$/);
    if (nullableMatch) {
        return simplifyType(nullableMatch[1]) + '?';
    }

    // Handle generic types: Name{A,B} → Name<A, B>
    const genericMatch = fqType.match(/^(.+?)\{(.+)\}$/);
    if (genericMatch) {
        const baseName = simplifyType(genericMatch[1]);
        const args = splitGenericArgs(genericMatch[2]).map(a => simplifyType(a));
        return `${baseName}<${args.join(', ')}>`;
    }

    // Generic parameter placeholders like ``0, ``1 → T, T2, etc.
    const doubleParam = fqType.match(/^``(\d+)$/);
    if (doubleParam) {
        const idx = parseInt(doubleParam[1]);
        return idx === 0 ? 'T' : `T${idx + 1}`;
    }
    const singleParam = fqType.match(/^`(\d+)$/);
    if (singleParam) {
        const idx = parseInt(singleParam[1]);
        return idx === 0 ? 'T' : `T${idx + 1}`;
    }

    // Keyword map
    if (KEYWORD_MAP[fqType]) return KEYWORD_MAP[fqType];

    // Strip namespace: take last segment after '.'
    const lastDot = fqType.lastIndexOf('.');
    if (lastDot >= 0) return fqType.substring(lastDot + 1);

    return fqType;
}

/** Split generic args respecting nested braces: "A,B{C,D}" → ["A", "B{C,D}"] */
function splitGenericArgs(str) {
    const args = [];
    let depth = 0;
    let current = '';
    for (const ch of str) {
        if (ch === '{') { depth++; current += ch; }
        else if (ch === '}') { depth--; current += ch; }
        else if (ch === ',' && depth === 0) { args.push(current); current = ''; }
        else { current += ch; }
    }
    if (current) args.push(current);
    return args;
}

/** Simplify an entire method signature: name(params) and generic arity */
function simplifySignature(name) {
    // Split into method name and parameter list first
    const parenIdx = name.indexOf('(');
    let methodName = parenIdx >= 0 ? name.substring(0, parenIdx) : name;
    const paramPart = parenIdx >= 0 ? name.substring(parenIdx) : '';

    // Handle generic arity on method name: MethodName``1 → MethodName<T>
    methodName = methodName.replace(/``(\d+)/, (_, n) => {
        const count = parseInt(n);
        const params = Array.from({ length: count }, (_, i) => i === 0 ? 'T' : `T${i + 1}`);
        return `<${params.join(', ')}>`;
    });

    // Handle parameter list in parentheses
    if (paramPart) {
        const paramStr = paramPart.substring(1, paramPart.length - 1);
        if (paramStr === '') return `${methodName}()`;
        const params = splitGenericArgs(paramStr).map(p => simplifyType(p.trim()));
        return `${methodName}(${params.join(', ')})`;
    }

    return methodName;
}

// --- Main ---

function main() {
    const args = parseArgs();
    const rootDir = path.resolve(args.root);

    if (!fs.existsSync(rootDir)) {
        console.error(`Root directory not found: ${rootDir}`);
        process.exit(1);
    }

    console.log(`Discovering projects under ${rootDir}...`);
    const csprojFiles = findProjects(rootDir);
    console.log(`  Found ${csprojFiles.length} projects`);

    const assemblies = [];
    let totalTypes = 0;

    for (const csprojPath of csprojFiles.sort()) {
        const projectDir = path.dirname(csprojPath);
        const projectName = path.basename(csprojPath, '.csproj');

        if (isExcluded(projectName, args.exclude)) {
            continue;
        }

        const csFiles = findCsFiles(projectDir);
        if (csFiles.length === 0) continue;

        const publicApiTypes = findPublicApiTypes(csFiles);
        if (publicApiTypes.size === 0) continue;

        const assemblyName = getAssemblyName(csprojPath);
        const xmlPath = findXmlDoc(projectDir, assemblyName);

        let xmlMembers = new Map();
        if (xmlPath) {
            xmlMembers = parseXmlDoc(xmlPath);
        } else {
            console.warn(`  WARNING: No XML doc found for ${assemblyName} (build with -c Release first)`);
        }

        const namespaces = findPublicApiNamespaces(csFiles);
        totalTypes += publicApiTypes.size;

        console.log(`  ${assemblyName}: ${publicApiTypes.size} [PublicApi] types, ${xmlMembers.size} XML members`);

        assemblies.push({
            name: assemblyName,
            publicApiTypes,
            namespaces,
            xmlMembers,
        });
    }

    if (assemblies.length === 0) {
        console.error('No projects with [PublicApi] types found.');
        process.exit(1);
    }

    console.log(`\nTotal: ${assemblies.length} assemblies, ${totalTypes} [PublicApi] types`);

    // Write manifest if requested (type-level only, for drift detection)
    if (args.manifest) {
        const manifest = {
            assemblies: assemblies.map(a => a.name).sort(),
            types: assemblies.flatMap(a => [...a.publicApiTypes]).sort(),
        };
        const manifestPath = path.resolve(args.manifest);
        fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n', 'utf8');
        console.log(`Manifest written to ${manifestPath} (${manifest.assemblies.length} assemblies, ${manifest.types.length} types)`);
    }

    // Generate markdown (skip if --out not provided and --manifest was the only goal)
    if (args.out || !args.manifest) {
        const markdown = generateMarkdown(assemblies);

        if (args.out) {
            const outPath = path.resolve(args.out);
            fs.writeFileSync(outPath, markdown, 'utf8');
            console.log(`Written to ${outPath} (${markdown.length} bytes)`);
        } else {
            process.stdout.write(markdown);
        }
    }
}

main();
