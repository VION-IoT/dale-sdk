#requires -Version 7
<#
.SYNOPSIS
  Exit 1 if an MSBuild file this repository PACKS under a NuGet build folder is not well-formed
  XML. A consumer's very first build imports that file, so a malformed one fails every project
  that references the package, with `MSB4024` and nothing else.

  The shape it catches: `--` inside an XML comment. XML forbids it, and the two comments that
  carried it both began on the literal name of a command-line switch - `--exclude-development-only`
  in one pass, `--package-id` in the INTRO area pass, which is the one that reached nuget.org as
  0.12.0. Both read as ordinary prose to every human and every tool this repository ran.

  Why nothing else sees it. No project here imports its own `build/*.targets`: the SDK's own
  projects use `ProjectReference`, and `examples/` reference a PUBLISHED package, so the packed
  targets file is consumed for the first time by a consumer who is not this repository. The
  solution build, the whole test suite, the style gate and `verify-packages` were all green with
  0.12.0's targets file unloadable - `verify-packages` reads assembly versions out of the packed
  artifact and never asks whether its MSBuild files parse.

  This gate is the cheap early rung: it reads the working tree on every pull request, before a
  package exists. It is not the whole rung - well-formed XML is necessary, not sufficient, and an
  MSBuild file can parse and still fail to import (an unknown element is `MSB4067`, not `MSB4024`).
  A real pack-and-consume round trip is the only thing that proves an import, and it belongs in a
  pre-public release regression suite rather than in `verify-packages`, which runs after both
  pushes and so can report a bad release without preventing one - see `docs/specs/_findings.md`
  § `RELEASE`.

  What it scans is DERIVED, not listed: every `Pack="true"` item whose `PackagePath` lands in
  `build/`, `buildTransitive/` or `buildMultiTargeting/`. A file added to one of those folders is
  covered the day it is packed, with nothing here to update - and a derivation that silently stops
  matching is what the floors below exist for.

  The derivation reads the four spellings MSBuild honours, because a gate covering only the one
  spelling in the tree today covers the tree today and nothing anyone adds tomorrow:

    - `Include=` and `Update=`. `build/Vion.Dale.Sdk.targets` is ALREADY a `None` item through the
      SDK's default glob, so `Update=` is the canonical way to add `Pack` metadata to it - and it
      is the spelling an author reaches for second.
    - metadata written as attributes, and as child elements. An IDE writes the child-element form.
    - a `PackagePath` carrying several `;`-separated targets. Every one is tested, not the first:
      `contentFiles/;build/` packs into a build folder just as surely as `build/` alone does.
    - the item declared in `Directory.Build.props` / `Directory.Build.targets` as well as in a
      project. This repository already declares packed content in one (`Directory.Build.targets:14`
      packs the shared readme), so that file is a real declaration site here, not a hypothetical.

  Where it still stops: a project kind other than `.csproj`, an `Include` composed from an MSBuild
  property, and a `.nuspec` driving the pack directly. The first two are reported rather than
  assumed - see the notes the report prints - and none of the three exists in this repository.

  Scans the tracked files (`git ls-files`); with -RepoRoot outside a git repo it walks the tree,
  skipping bin/, obj/, node_modules/ and .git/ (the self-test's path).
#>
[CmdletBinding()]
param(
    [string]$RepoRoot
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) {
    $RepoRoot = git rev-parse --show-toplevel 2>$null
    if (-not $RepoRoot) { Write-Host 'packed-msbuild-lint: not inside a git repo - pass -RepoRoot'; exit 2 }
    $RepoRoot = $RepoRoot.Trim()
}
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

# The folders NuGet imports from. A package's `build/` is auto-imported into the consuming
# project; `buildTransitive/` is the same for a transitive reference, and `buildMultiTargeting/`
# for an outer build. All three are imported as MSBuild, so all three must parse.
$packFolders = @('build', 'buildtransitive', 'buildmultitargeting')
# The kinds MSBuild imports out of those folders. A `build/` folder may legitimately carry a
# non-MSBuild payload; that is not this gate's rule, and the tally below names any it found so
# the coverage it does NOT claim is visible rather than assumed.
$msbuildKinds = @('.targets', '.props')

# The files that declare packed items: projects, and the two well-known files whose contents
# MSBuild imports into every project beneath them. Named rather than globbed on `.props`/`.targets`
# generally, because a packed targets file is itself a `.targets` - scanning those as declaration
# sites would report the founding defect as a malformed DECLARATION rather than as the malformed
# packed file it is, which is the wrong sentence in front of the right author.
$declarationNames = @('directory.build.props', 'directory.build.targets')
function Test-Declaration([string]$path) {
    if ([System.IO.Path]::GetExtension($path).ToLowerInvariant() -eq '.csproj') { return $true }
    return $declarationNames -contains ([System.IO.Path]::GetFileName($path).ToLowerInvariant())
}

$projects = @()
$inGit = $false
Push-Location $RepoRoot
try {
    $top = git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -eq 0 -and $top -and ([System.IO.Path]::GetFullPath($top.Trim()).TrimEnd('\', '/') -eq $RepoRoot.TrimEnd('\', '/'))) {
        $inGit = $true
        $projects = @(git ls-files -z | ForEach-Object { $_ }) -split "`0" |
            Where-Object { $_ } | ForEach-Object { Join-Path $RepoRoot $_ } | Where-Object { Test-Declaration $_ }
    }
}
finally { Pop-Location }
if (-not $inGit) {
    # -Force, for the reason bom-lint carries it: on Unix a dot-prefixed directory is hidden and
    # Get-ChildItem omits it without the switch, so a walk is quietly OS-dependent.
    $projects = @(Get-ChildItem -LiteralPath $RepoRoot -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git)[\\/]' } |
        Where-Object { Test-Declaration $_.FullName } | ForEach-Object { $_.FullName })
}

function Get-Relative([string]$path) {
    return ($path.Substring($RepoRoot.Length).TrimStart('\', '/')) -replace '\\', '/'
}

# Item metadata has two spellings and MSBuild honours both: an attribute on the item, and a child
# element under it. An IDE writes the second, so a gate reading only attributes is blind to
# whatever the IDE last rewrote. `local-name()` so this reads a project with the old MSBuild
# xmlns as well as an SDK-style one.
function Get-Metadata($node, [string]$name) {
    $attr = $node.GetAttribute($name)
    if ($attr) { return $attr }
    $child = $node.SelectSingleNode("*[local-name()='$name']")
    if ($child) { return $child.InnerText.Trim() }
    return ''
}

$problems = [System.Collections.Generic.List[string]]::new()
$entries = 0
$unresolved = [System.Collections.Generic.List[string]]::new()
$otherKinds = [System.Collections.Generic.List[string]]::new()
$targets = [System.Collections.Generic.List[string]]::new()

foreach ($proj in $projects) {
    if (-not (Test-Path -LiteralPath $proj)) { continue }
    $projRel = Get-Relative $proj
    $doc = [System.Xml.XmlDocument]::new()
    try { $doc.Load($proj) }
    catch [System.Xml.XmlException] {
        # A project file that does not parse is the same defect one step earlier, and the
        # derivation below cannot read it - so it is reported rather than skipped.
        $problems.Add("${projRel}:$($_.Exception.LineNumber):$($_.Exception.LinePosition): the project file itself is not well-formed XML - $($_.Exception.Message)")
        continue
    }
    catch {
        # Anything else the read can raise - a permission, a locked file. Without this the gate
        # still fails, because $ErrorActionPreference is Stop, but it fails as a raw PowerShell
        # stack with no line of its own, which reads as the gate being broken rather than the tree.
        $problems.Add("${projRel}: could not be read - $($_.Exception.Message)")
        continue
    }
    # `//*` matches an element in any namespace, so this reads an SDK-style project and one
    # carrying the old MSBuild xmlns alike.
    foreach ($node in $doc.SelectNodes('//*')) {
        $pack = Get-Metadata $node 'Pack'
        if ($pack -notmatch '^(?i:true)$') { continue }
        $packagePath = Get-Metadata $node 'PackagePath'
        if (-not $packagePath) { continue }
        # Include names files; Update adds metadata to files an earlier glob already matched, which
        # is how Pack metadata is most naturally attached to a file the SDK's default glob picked
        # up. Remove takes items away and packs nothing, so it is not a source.
        $include = Get-Metadata $node 'Include'
        if (-not $include) { $include = Get-Metadata $node 'Update' }
        if (-not $include) { continue }
        # Every target, not the first: a PackagePath may carry several, and a build folder anywhere
        # among them packs into a build folder.
        $landsInBuild = $false
        foreach ($target in (($packagePath -replace '\\', '/') -split ';')) {
            if (-not $target.Trim()) { continue }
            $firstSegment = ($target.Trim().TrimStart('/') -split '/')[0]
            if ($packFolders -contains $firstSegment.ToLowerInvariant()) { $landsInBuild = $true }
        }
        if (-not $landsInBuild) { continue }
        $entries++

        $relInclude = $include -replace '\\', '/'
        if ($relInclude -match '\$\(') {
            # An Include naming an MSBuild property cannot be resolved by a file scan. Named, not
            # dropped: an unresolvable entry is coverage this gate does not have, and a reader who
            # cannot see it will assume it does.
            $unresolved.Add("${projRel}: '$include' names an MSBuild property, so this scan cannot resolve it")
            continue
        }
        $projDir = Split-Path -Parent $proj
        $matched = @()
        if ($relInclude -match '[*?]') {
            # A wildcard Include is resolved against the project directory the way MSBuild globs
            # it. `**` is a recursive walk; a plain `*` is not.
            $recurse = $relInclude -match '\*\*'
            $pattern = ($relInclude -replace '\*\*/', '' -replace '\*\*', '*')
            $leaf = Split-Path -Leaf $pattern
            $base = Split-Path -Parent $pattern
            $searchRoot = if ($base) { Join-Path $projDir $base } else { $projDir }
            if (Test-Path -LiteralPath $searchRoot) {
                $matched = @(Get-ChildItem -LiteralPath $searchRoot -File -Force -Filter $leaf -Recurse:$recurse -ErrorAction SilentlyContinue |
                    ForEach-Object { $_.FullName })
            }
            if (-not $matched.Count) {
                # The same defect the literal branch reports, and it has to be reported here too or
                # the two branches disagree about a shipped package missing its import.
                $problems.Add("${projRel}: packs '$include' under '$packagePath' and the pattern matches no file")
                continue
            }
        }
        else {
            $resolved = Join-Path $projDir $relInclude
            if (Test-Path -LiteralPath $resolved -PathType Leaf) { $matched = @([System.IO.Path]::GetFullPath($resolved)) }
            else {
                # An Include pointing at nothing packs nothing, and a targets file that has moved
                # without its Include moving is a shipped package missing its import.
                $problems.Add("${projRel}: packs '$include' under '$packagePath' and no such file exists")
                continue
            }
        }
        foreach ($m in $matched) {
            $ext = [System.IO.Path]::GetExtension($m).ToLowerInvariant()
            if ($msbuildKinds -notcontains $ext) { $otherKinds.Add((Get-Relative $m)); continue }
            if (-not $targets.Contains($m)) { $targets.Add($m) }
        }
    }
}

$parseFailures = 0
foreach ($path in $targets) {
    $rel = Get-Relative $path
    $doc = [System.Xml.XmlDocument]::new()
    try { $doc.Load($path) }
    catch [System.Xml.XmlException] {
        # The same reader MSBuild imports with, so this message is the one MSB4024 quotes back to
        # the consumer - and the line and column are where to go.
        $problems.Add("${rel}:$($_.Exception.LineNumber):$($_.Exception.LinePosition): $($_.Exception.Message)")
        $parseFailures++
    }
    catch {
        $problems.Add("${rel}: could not be read - $($_.Exception.Message)")
    }
}

$tally = "$($targets.Count) packed MSBuild file(s) from $entries entr(ies) across $($projects.Count) project file(s)"
# Printed, not just floored, for the reason bom-lint prints its kinds: a floor only catches a
# count reaching zero, and a derivation narrows in practice by dropping SOME of what it matched.
foreach ($u in $unresolved) { Write-Host "  note: $u" }
foreach ($o in ($otherKinds | Sort-Object -Unique)) { Write-Host "  note: '$o' is packed under a build folder and is not an MSBuild kind - not parsed" }

# What was actually found is reported BEFORE the floors below. A floor exists to fail a scan that
# judged nothing, and an entry whose file is missing drives the count of parsed files to zero
# while being a finding in its own right - reporting the floor there would replace the defect's
# own message with a note about the scan.
if ($problems.Count) {
    Write-Host "packed-msbuild-lint: FAIL - $($problems.Count) problem(s) across $tally`:"
    $problems | Sort-Object | ForEach-Object { Write-Host "  $_" }
    # Only where something actually failed to PARSE. A missing Include and a malformed project file
    # are both real and neither reaches a consumer as MSB4024 - the first is a package shipped
    # without the import it declares, the second fails this repository's own pack.
    if ($parseFailures) { Write-Host '  A consumer importing this package fails with MSB4024 on its first build.' }
    exit 1
}

# Anti-vacuous floors, the ones bom-lint and pragma-reason-lint carry. Every step of the
# derivation can stop matching without anything failing, and each failure reads exactly like a
# clean repository from the outside: no project files reached the scan, no project declares a
# packed build entry any more, or the entries resolve to no MSBuild file at all. The middle floor
# is the one this gate needs most - the whole scan hangs off one attribute pair on one item, and
# a rename of either leaves the gate reporting OK forever.
if ($projects.Count -eq 0) {
    Write-Host 'packed-msbuild-lint: FAIL - ZERO project file(s) reached the scan (nothing was judged - anti-vacuous floor).'
    exit 1
}
if ($entries -eq 0) {
    Write-Host "packed-msbuild-lint: FAIL - ZERO packed build-folder entr(ies) found across $($projects.Count) project file(s); this repository packs at least one, so the derivation is broken, not the tree (anti-vacuous floor)."
    exit 1
}
if ($targets.Count -eq 0) {
    Write-Host "packed-msbuild-lint: FAIL - $entries packed build-folder entr(ies) resolved to ZERO MSBuild file(s) to parse (anti-vacuous floor)."
    exit 1
}
Write-Host "packed-msbuild-lint: OK - $tally, all well-formed XML"
exit 0
