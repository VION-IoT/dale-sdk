param(
    [Parameter(Mandatory=$false)]
    [string]$OutputDir = "./packages/examples",

    [Parameter(Mandatory=$false)]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$projects = @(
    "examples\Vion.Examples.PingPong\Vion.Examples.PingPong\Vion.Examples.PingPong.csproj",
    "examples\Vion.Examples.ToggleLight\Vion.Examples.ToggleLight\Vion.Examples.ToggleLight.csproj",
    "examples\Vion.Examples.Energy\Vion.Examples.Energy\Vion.Examples.Energy.csproj",
    "examples\Vion.Examples.ModbusRtu\Vion.Examples.ModbusRtu\Vion.Examples.ModbusRtu.csproj",
    "examples\Vion.Examples.RichTypes\Vion.Examples.RichTypes\Vion.Examples.RichTypes.csproj"
)

# Resolve output directory to absolute path
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

Write-Host "Packing example projects ($Configuration) -> $OutputDir" -ForegroundColor Cyan
Write-Host ""

$packed = @()

foreach ($project in $projects) {
    if (-not (Test-Path $project)) {
        Write-Warning "Project not found: $project"
        continue
    }

    $name = [System.IO.Path]::GetFileNameWithoutExtension($project)
    Write-Host "Packing $name..." -ForegroundColor Yellow

    # Full build+publish+parse+pack pipeline (no --no-build, so Vion.Dale.Sdk.targets runs the LogicBlockParser)
    dotnet pack $project -c $Configuration -p:IsPackable=true -o $OutputDir 2>&1 | ForEach-Object {
        if ($_ -match "error") {
            Write-Host "  $_" -ForegroundColor Red
        } elseif ($_ -match "Successfully created package") {
            Write-Host "  $_" -ForegroundColor Green
            $packed += $_
        }
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to pack $name"
    }
}

Write-Host ""
Write-Host "$($packed.Count)/$($projects.Count) packages created in $OutputDir" -ForegroundColor Cyan
