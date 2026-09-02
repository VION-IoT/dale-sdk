#requires -Version 7
<#
.SYNOPSIS
  SDD change-doc helper (docs/spec-process.md). Ported from logic-block-libraries'
  spec-change.ps1, adapted to this repo's flat corpus: no per-library resolution,
  delta targets are repo-root-relative.

  spec-change.ps1 new <slug> [-Areas <csv>]
      Scaffold docs/changes/<today>-<slug>.md from docs/changes/_template.md with
      frontmatter filled.

  spec-change.ps1 archive <slug>
      Refuse unless the change is distilled: every ADDED/MODIFIED id in the
      "Spec delta" is present in its named target and, for an AC-/SYS- id, the
      line's EARS text is carried by the id's declaring bullet there (backticks,
      brackets, generic type arguments, wrapping, a GAP tail and a trailing
      parenthetical set aside; a MODIFIED line supersedes the ADDED line for the
      same id); every REMOVED id is absent. Then flip status to archived and
      move the doc to
      docs/changes/archive/. The text check exists because a criterion was
      reworded on a page with no MODIFIED line, so page and delta disagreed
      until a review noticed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)][ValidateSet('new', 'archive')][string]$Command,
    [Parameter(Mandatory, Position = 1)][string]$Slug,
    [string]$Areas,
    [string]$RepoRoot
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) {
    $RepoRoot = git rev-parse --show-toplevel 2>$null
    if (-not $RepoRoot) { Write-Host 'spec-change: not inside a git repo - pass -RepoRoot'; exit 2 }
    $RepoRoot = $RepoRoot.Trim()
}
$changesDir = Join-Path $RepoRoot 'docs/changes'
$today = Get-Date -Format 'yyyy-MM-dd'

# Usage errors exit 2 via Write-Host: under $ErrorActionPreference = 'Stop' a
# Write-Error would terminate with exit 1 — indistinguishable from the
# contractual "NOT distilled" refusal below.
if ($Command -eq 'new') {
    $tpl = Join-Path $changesDir '_template.md'
    if (-not (Test-Path $tpl)) { Write-Host "spec-change: template not found: $tpl"; exit 2 }
    $dest = Join-Path $changesDir "$today-$Slug.md"
    if (Test-Path $dest) { Write-Host "spec-change: already exists: $dest"; exit 2 }
    $body = (Get-Content -Raw $tpl) `
        -replace '(?m)^slug:.*$',    "slug: $Slug" `
        -replace '(?m)^created:.*$', "created: $today" `
        -replace '(?m)^updated:.*$', "updated: $today"
    if ($Areas) { $body = $body -replace '(?m)^areas:.*$', "areas: $Areas" }
    Set-Content -LiteralPath $dest -Value $body -NoNewline
    Write-Host "spec-change: created $dest"
    exit 0
}

# archive — top-level docs only; underscore-prefixed files are never change docs.
$doc = Get-ChildItem -Path $changesDir -Filter "*-$Slug.md" -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notlike '_*' } | Select-Object -First 1
if (-not $doc) { Write-Host "spec-change: no in-flight change doc matching '*-$Slug.md' under $changesDir"; exit 2 }
$raw = Get-Content -Raw $doc.FullName

# `<OP> <ID> -> <target> : <payload>` (leading `- ` bullet optional). Targets are
# repo-root-relative; a `.md` target is grepped for the id, any other existing-path
# target (a directory, a script) is checked for existence only. One effective line per
# id and target: the last in document order, except that a MODIFIED line is never
# superseded by an ADDED one — an amendment appends its MODIFIED below the original
# ADDED, and the MODIFIED text is the current one.
# A payload wrapped onto indented continuation lines (never a nested `- ` bullet) is read whole,
# so a clause on the second line is compared too.
$deltaRx = '(?m)^\s*-?\s*(ADDED|MODIFIED|REMOVED)\s+(\S+)\s*->\s*([^\s:]+)[ \t]*(?::[ \t]*(?<payload>[^\r\n]*(?:\r?\n[ \t]+(?!-\s)[^\r\n]*)*))?'
$specIdRx = '^(?:AC|SYS)-[A-Z0-9]+-\d+(?:\.\d+)?$'
$effective = [ordered]@{}
foreach ($m in [regex]::Matches($raw, $deltaRx)) {
    $op = $m.Groups[1].Value; $target = $m.Groups[3].Value.TrimEnd('/')
    # Delta lines conventionally backtick the id, but targets carry it plain — strip
    # backticks so the presence grep can match.
    $id = $m.Groups[2].Value.Trim('`')
    if ($id -match '[<>]' -or $target -match '[<>]') { continue }   # template placeholder line
    $key = "$id -> $target"
    if ($effective.Contains($key) -and $effective[$key].op -eq 'MODIFIED' -and $op -eq 'ADDED') { continue }
    $effective[$key] = @{ op = $op; id = $id; target = $target; payload = $m.Groups['payload'].Value.Trim() }
}

# What a delta line and a page bullet share once formatting is set aside: backticks, the
# brackets and generic arguments a page adds when it writes a type out (`[ServiceInterface]`,
# `IChangeThreshold<T>` — one pass's delta lines omitted them five times), line wrapping
# (pages wrap at 100 columns), a `GAP:` tail, and — on the delta side only — a trailing
# parenthetical, where passes annotate provenance ("(row 32 fix)"). Dropping text from the
# delta side alone can only make the containment check more lenient.
function Get-ComparableText([string]$s, [bool]$dropTrailingParenthetical) {
    $s = $s -replace '`', ''
    $s = $s -replace '[\[\]]', ''
    $s = $s -replace '<\w+(?:\s*,\s*\w+)*>', ''
    $s = $s -replace '\s*\bGAP:.*$', ''
    if ($dropTrailingParenthetical) { $s = $s -replace '\s*\([^()]*\)\s*$', '' }
    return ($s -replace '\s+', ' ').Trim()
}
# The bullet declaring an id on a page — `- `ID` (Kind): text` — with its indented
# continuation lines folded in. `AC-X-001.1` must not match the bullet of `AC-X-001.10`.
function Get-DeclaringBullet([string[]]$lines, [string]$id) {
    $startRx = '^\s*-\s+`?' + [regex]::Escape($id) + '`?(?![\w.])'
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch $startRx) { continue }
        $text = $lines[$i].Trim()
        for ($j = $i + 1; $j -lt $lines.Count -and $lines[$j] -match '^\s+\S' -and $lines[$j] -notmatch '^\s*-\s'; $j++) {
            $text += ' ' + $lines[$j].Trim()
        }
        return $text
    }
    return $null
}

$unapplied = [System.Collections.Generic.List[string]]::new()
foreach ($e in $effective.Values) {
    $op = $e.op; $id = $e.id; $target = $e.target
    $path = Join-Path $RepoRoot $target
    if ($target -like '*.md') {
        $present = (Test-Path $path) -and ((Get-Content -Raw $path) -match [regex]::Escape($id))
    } else {
        $present = Test-Path $path
    }
    if ($op -eq 'REMOVED') {
        if ($present -and $target -like '*.md') { $unapplied.Add("$op $id still present in $target") }
        continue
    }
    if (-not $present) { $unapplied.Add("$op $id not found in $target"); continue }
    # The text check: spec ids on .md targets with a payload. Anything else (a script target,
    # an ad-hoc label, a bare line) keeps the presence check above.
    if ($target -notlike '*.md' -or $id -notmatch $specIdRx -or -not $e.payload) { continue }
    $bullet = Get-DeclaringBullet (Get-Content $path) $id
    if ($null -eq $bullet) { $unapplied.Add("$op $id has no declaring bullet in $target (the id appears there, but not as a ``- $id`` line)"); continue }
    $want = Get-ComparableText $e.payload $true
    $have = Get-ComparableText $bullet $false
    if (-not $have.Contains($want)) {
        $unapplied.Add("$op $id text differs from $target`n      delta: $want`n      page : $have")
    }
}
if ($unapplied.Count) {
    Write-Host "spec-change: NOT distilled - cannot archive ($($unapplied.Count) unapplied delta line(s)):"
    $unapplied | ForEach-Object { Write-Host "  $_" }
    exit 1
}

$archiveDir = Join-Path $changesDir 'archive'
New-Item -ItemType Directory -Force -Path $archiveDir | Out-Null
Set-Content -LiteralPath $doc.FullName -NoNewline -Value ($raw -replace '(?m)^status:.*$', 'status: archived')
# `git mv` requires a TRACKED file; the default one-PR flow archives a doc that was
# never committed (created + distilled + archived in the same PR), where git mv
# fatals — falling through would leave a half-archived state (status flipped, file
# not moved) that spec-lint then trips on.
$dest = Join-Path $archiveDir $doc.Name
git -C $RepoRoot mv $doc.FullName $dest 2>$null
if ($LASTEXITCODE -ne 0) { Move-Item -LiteralPath $doc.FullName -Destination $dest }
# Stage the destination explicitly: `git mv` stages the rename from the INDEX blob, so
# the status edit written above lands UNSTAGED at the new path — a plain `git commit -m`
# then ships an archived doc still carrying `status: in-flight` and CI goes red.
git -C $RepoRoot add -- $dest 2>$null
Write-Host "spec-change: archived $($doc.Name) -> archive/ (staged)"

# Rewrite markdown links that still point at the PRE-archive path — the move breaks
# every `changes/<doc>` link in tracked docs.
$docName = [regex]::Escape($doc.Name)
$linkRx = "(?<pre>\((?:[^()\s]*[/\\])?)(?<changes>changes/)(?<name>$docName)(?<post>[)#])"
$tracked = @(git -C $RepoRoot ls-files '*.md' 2>$null)
if ($LASTEXITCODE -ne 0) { $tracked = @() }
$rewritten = 0
foreach ($md in $tracked) {
    $p = Join-Path $RepoRoot $md
    if ((Resolve-Path -LiteralPath $p).Path -eq (Resolve-Path -LiteralPath $dest).Path) { continue }
    $content = Get-Content -Raw -LiteralPath $p
    $updated = [regex]::Replace($content, $linkRx, '${pre}changes/archive/${name}${post}')
    if ($updated -ne $content) {
        Set-Content -LiteralPath $p -NoNewline -Value $updated
        $rewritten++
        Write-Host "spec-change: rewrote pre-archive link(s) in $md"
    }
}
if ($rewritten -eq 0) { Write-Host "spec-change: no stale links to the pre-archive path" }

# Rewrite the moved doc's OWN outbound relative links — the move one level down into
# archive/ adds one `../` to every relative target that is not itself in archive/.
$content = Get-Content -Raw -LiteralPath $dest
$updated = $content
# Every pre-existing `](../…)` chain gains one level (runs FIRST so the passes below
# never feed it their own output).
$updated = $updated -replace '\]\(\.\./', '](../../'
# `](archive/X)` becomes a sibling `](X)`.
$updated = $updated -replace '\]\(archive/', ']('
# Remaining bare `](<date>-… .md)` sibling links move up one level unless the target
# file itself lives in archive/ (then the sibling name is already right).
$updated = [regex]::Replace($updated, '\]\((?!\.\./)(?!https?:)(?!#)(?<t>\d{4}-[^)#\s]+\.md)(?<post>[)#])', {
        param($m)
        if (Test-Path -LiteralPath (Join-Path $archiveDir $m.Groups['t'].Value)) { $m.Value } else { "](../$($m.Groups['t'].Value)$($m.Groups['post'].Value)" }
    })
if ($updated -ne $content) {
    Set-Content -LiteralPath $dest -NoNewline -Value $updated
    git -C $RepoRoot add -- $dest 2>$null
    Write-Host "spec-change: rewrote the archived doc's own outbound relative links for the new depth"
}
exit 0
