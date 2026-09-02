#requires -Version 7
# Self-test for doc-comment-lint.ps1 (one <summary> per doc block). Plain pwsh, NOT Pester.
# Run all: `pwsh -File scripts/run-script-tests.ps1`; just this one:
# `pwsh -File scripts/doc-comment-lint.tests.ps1`.
$ErrorActionPreference = 'Stop'
$lint = Join-Path $PSScriptRoot 'doc-comment-lint.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("doccomment-" + [guid]::NewGuid().ToString('N'))

function New-File($rel, $content) {
    $p = Join-Path $tmp $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $p) | Out-Null
    Set-Content -LiteralPath $p -Value $content -NoNewline
    return $p
}
function Invoke-Lint {
    pwsh -NoProfile -File $lint -RepoRoot $tmp | Out-Null
    return $LASTEXITCODE
}

try {
    # Case 1: two members, one <summary> each, attributes between doc comment and declaration -> 0
    New-File 'Fake.Sdk/Clean.cs' @'
namespace Fake
{
    /// <summary>
    ///     A type.
    /// </summary>
    [PublicApi]
    public class Clean
    {
        /// <summary>Does one thing.</summary>
        /// <param name="x">the thing</param>
        [Obsolete]
        public void One(int x)
        {
        }

        /// <summary>Does another.</summary>
        public void Two()
        {
        }
    }
}
'@ | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 1 (one summary per block) expected 0" }

    # Case 2: an insertion below a doc comment - the new member carries two adjacent <summary> blocks -> 1
    $stolen = New-File 'Fake.Sdk/Stolen.cs' @'
namespace Fake
{
    public class Stolen
    {
        /// <summary>
        ///     Belongs to Old.
        /// </summary>
        /// <summary>
        ///     Belongs to Inserted.
        /// </summary>
        public void Inserted()
        {
        }

        public void Old()
        {
        }
    }
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 2 (two adjacent summaries) expected 1" }

    # Case 3: the same theft with a blank line between the two runs - the compiler still attaches both -> 1
    Set-Content -LiteralPath $stolen -NoNewline -Value @'
namespace Fake
{
    public class Stolen
    {
        /// <summary>Belongs to Old.</summary>

        /// <summary>Belongs to Inserted.</summary>
        public void Inserted()
        {
        }
    }
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 3 (blank line between the runs) expected 1" }

    # Case 4: a self-closing <summary/> beside a full one is still two -> 1
    Set-Content -LiteralPath $stolen -NoNewline -Value @'
namespace Fake
{
    public class Stolen
    {
        /// <summary/>
        /// <summary>Second.</summary>
        public void Inserted()
        {
        }
    }
}
'@
    if ((Invoke-Lint) -ne 1) { throw "Case 4 (self-closing plus full) expected 1" }
    Remove-Item $stolen

    # Case 5: the word summary in prose, and a <see cref> naming a Summary member, are not elements -> 0
    New-File 'Fake.Sdk/Prose.cs' @'
namespace Fake
{
    public class Prose
    {
        /// <summary>
        ///     Reads the summary above; see <see cref="Summary"/> and <c>&lt;summary&gt;</c>.
        /// </summary>
        public string Summary => "";
    }
}
'@ | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 5 (summary as prose) expected 0" }

    # Case 6: build output is skipped - a double summary under bin/ does not fail -> 0
    New-File 'Fake.Sdk/bin/Debug/Generated.cs' @'
/// <summary>a</summary>
/// <summary>b</summary>
public class Generated
{
}
'@ | Out-Null
    if ((Invoke-Lint) -ne 0) { throw "Case 6 (bin/ skipped) expected 0" }

    # Case 7: a doc block the file ends on is still closed and counted -> 1
    New-File 'Fake.Sdk/Tail.cs' @'
public class Tail
{
}
/// <summary>a</summary>
/// <summary>b</summary>
'@ | Out-Null
    if ((Invoke-Lint) -ne 1) { throw "Case 7 (block at end of file) expected 1" }

    Write-Host 'doc-comment-lint.tests: PASS'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
