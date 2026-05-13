param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [ValidateSet("references")]
    [string]$Scope = "references"
)

# Updates <PackageReference> versions in templates and examples after a new
# Vion.Dale.* SDK release is published to the feed. SDK project versions
# themselves are no longer stored in .csproj — they come from the git tag
# at pack time (see .github/workflows/publish.yml and README "Releases").

$ErrorActionPreference = "Stop"

# Template projects that reference Vion.Dale.* packages.
# Note: the template is distributed ONLY as content bundled inside Vion.Dale.Cli.
# The CLI rewrites these PackageReferences to its own version at pack time (see
# Vion.Dale.Cli.csproj), so the values here only matter for the main sln build
# and for local `dale new` invocations against a dev CLI.
$templateProjects = @(
    @{
        Path              = "templates\vion-iot-library\VionIotLibraryTemplate\VionIotLibraryTemplate.csproj"
        PackageReferences = @("Vion.Dale.Sdk", "Vion.Dale.Sdk.DigitalIo")
    },
    @{
        Path              = "templates\vion-iot-library\VionIotLibraryTemplate.DevHost\VionIotLibraryTemplate.DevHost.csproj"
        PackageReferences = @("Vion.Dale.DevHost.Web")
    },
    @{
        Path              = "templates\vion-iot-library\VionIotLibraryTemplate.Test\VionIotLibraryTemplate.Test.csproj"
        PackageReferences = @("Vion.Dale.Sdk.TestKit", "Vion.Dale.Sdk.DigitalIo.TestKit")
    }
)

# Example projects that reference Vion.Dale.* packages.
$exampleProjects = @(
    # PingPong example
    @{
        Path              = "examples\Vion.Examples.PingPong\Vion.Examples.PingPong\Vion.Examples.PingPong.csproj"
        PackageReferences = @("Vion.Dale.Sdk", "Vion.Dale.Sdk.DigitalIo")
    },
    @{
        Path              = "examples\Vion.Examples.PingPong\Vion.Examples.PingPong.DevHost\Vion.Examples.PingPong.DevHost.csproj"
        PackageReferences = @("Vion.Dale.DevHost.Web")
    },
    @{
        Path              = "examples\Vion.Examples.PingPong\Vion.Examples.PingPong.Test\Vion.Examples.PingPong.Test.csproj"
        PackageReferences = @("Vion.Dale.Sdk.TestKit", "Vion.Dale.Sdk.DigitalIo.TestKit")
    },
    # Energy example
    @{
        Path              = "examples\Vion.Examples.Energy\Vion.Examples.Energy\Vion.Examples.Energy.csproj"
        PackageReferences = @("Vion.Dale.Sdk", "Vion.Dale.Sdk.Http", "Vion.Dale.Sdk.DigitalIo", "Vion.Dale.Sdk.AnalogIo")
    },
    @{
        Path              = "examples\Vion.Examples.Energy\Vion.Examples.Energy.DevHost\Vion.Examples.Energy.DevHost.csproj"
        PackageReferences = @("Vion.Dale.DevHost.Web")
    },
    @{
        Path              = "examples\Vion.Examples.Energy\Vion.Examples.Energy.Test\Vion.Examples.Energy.Test.csproj"
        PackageReferences = @("Vion.Dale.Sdk.TestKit", "Vion.Dale.Sdk.DigitalIo.TestKit", "Vion.Dale.Sdk.AnalogIo.TestKit")
    },
    # ToggleLight example
    @{
        Path              = "examples\Vion.Examples.ToggleLight\Vion.Examples.ToggleLight\Vion.Examples.ToggleLight.csproj"
        PackageReferences = @("Vion.Dale.Sdk", "Vion.Dale.Sdk.DigitalIo")
    },
    @{
        Path              = "examples\Vion.Examples.ToggleLight\Vion.Examples.ToggleLight.DevHost\Vion.Examples.ToggleLight.DevHost.csproj"
        PackageReferences = @("Vion.Dale.DevHost.Web")
    },
    @{
        Path              = "examples\Vion.Examples.ToggleLight\Vion.Examples.ToggleLight.Test\Vion.Examples.ToggleLight.Test.csproj"
        PackageReferences = @("Vion.Dale.Sdk.TestKit", "Vion.Dale.Sdk.DigitalIo.TestKit")
    },
    # ModbusRtu example
    @{
        Path              = "examples\Vion.Examples.ModbusRtu\Vion.Examples.ModbusRtu\Vion.Examples.ModbusRtu.csproj"
        PackageReferences = @("Vion.Dale.Sdk", "Vion.Dale.Sdk.Modbus.Rtu")
    },
    @{
        Path              = "examples\Vion.Examples.ModbusRtu\Vion.Examples.ModbusRtu.DevHost\Vion.Examples.ModbusRtu.DevHost.csproj"
        PackageReferences = @("Vion.Dale.DevHost.Web")
    },
    @{
        Path              = "examples\Vion.Examples.ModbusRtu\Vion.Examples.ModbusRtu.Test\Vion.Examples.ModbusRtu.Test.csproj"
        PackageReferences = @("Vion.Dale.Sdk.TestKit", "Vion.Dale.Sdk.Modbus.Rtu.TestKit")
    },
    # Presentation example (no Test project — pack/upload only; demonstrates declarative-presentation surface)
    @{
        Path              = "examples\Vion.Examples.Presentation\Vion.Examples.Presentation\Vion.Examples.Presentation.csproj"
        PackageReferences = @("Vion.Dale.Sdk")
    },
    @{
        Path              = "examples\Vion.Examples.Presentation\Vion.Examples.Presentation.DevHost\Vion.Examples.Presentation.DevHost.csproj"
        PackageReferences = @("Vion.Dale.DevHost.Web")
    },
    # RichTypes example (no Test project — pack/upload only)
    @{
        Path              = "examples\Vion.Examples.RichTypes\Vion.Examples.RichTypes\Vion.Examples.RichTypes.csproj"
        PackageReferences = @("Vion.Dale.Sdk")
    },
    @{
        Path              = "examples\Vion.Examples.RichTypes\Vion.Examples.RichTypes.DevHost\Vion.Examples.RichTypes.DevHost.csproj"
        PackageReferences = @("Vion.Dale.DevHost.Web")
    }
)

function Set-ProjectVersion
{
    param(
        [string]$projectPath,
        [string]$version
    )

    $content = Get-Content $projectPath -Raw
    # Update <Version>
    $newContent = $content -replace '<Version>[^<]+</Version>', "<Version>$version</Version>"
    if ($content -eq $newContent)
    {
        Write-Warning "  No <Version> element found in $( Split-Path $projectPath -Leaf )"
        return
    }
    $newContent | Set-Content $projectPath -NoNewline
    Write-Host "  [+] Updated $( Split-Path $projectPath -Leaf ) <Version> to $version" -ForegroundColor Green
}

function Update-PackageReference
{
    param(
        [string]$projectPath,
        [string]$packageId,
        [string]$version
    )

    $content = Get-Content $projectPath -Raw
    $pattern = '(<PackageReference\s+Include="' + [regex]::Escape($packageId) + '"\s+Version=")[^"]+(")'
    $replacement = "`${1}$version`${2}"
    $newContent = $content -replace $pattern, $replacement

    if ($content -eq $newContent)
    {
        Write-Warning "  No PackageReference for $packageId found in $( Split-Path $projectPath -Leaf )"
        return $false
    }

    $newContent | Set-Content $projectPath -NoNewline
    Write-Host "  [+] $packageId -> $version in $( Split-Path $projectPath -Leaf )" -ForegroundColor Green
    return $true
}

function Clear-NuGetPackageCache
{
    param([string]$version)

    $sdkPackageIds = @(
        "Vion.Dale.Sdk",
        "Vion.Dale.Sdk.Http",
        "Vion.Dale.Sdk.DigitalIo",
        "Vion.Dale.Sdk.DigitalIo.TestKit",
        "Vion.Dale.Sdk.AnalogIo",
        "Vion.Dale.Sdk.AnalogIo.TestKit",
        "Vion.Dale.Sdk.Modbus.Core",
        "Vion.Dale.Sdk.Modbus.Tcp",
        "Vion.Dale.Sdk.Modbus.Rtu",
        "Vion.Dale.Sdk.Modbus.Rtu.TestKit",
        "Vion.Dale.Sdk.TestKit",
        "Vion.Dale.DevHost.Web"
    )

    Write-Host "`nClearing NuGet cache for version $version..." -ForegroundColor Yellow
    $globalPackagesDir = Join-Path $env:USERPROFILE ".nuget\packages"
    foreach ($packageId in $sdkPackageIds)
    {
        $cacheDir = Join-Path $globalPackagesDir "$( $packageId.ToLower() )\$version"
        if (Test-Path $cacheDir)
        {
            Remove-Item $cacheDir -Recurse -Force
            Write-Host "  [+] Cleared $packageId/$version" -ForegroundColor Green
        }
    }

    Write-Host "  Clearing HTTP cache..." -ForegroundColor Gray
    dotnet nuget locals http-cache --clear | Out-Null
    Write-Host "  [+] HTTP cache cleared" -ForegroundColor Green
}

# Validate version format — allow SemVer pre-release suffixes (e.g. 0.2.0-preview.1)
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')
{
    throw "Version must be SemVer (e.g. 0.2.0 or 0.2.0-preview.1), got: $Version"
}

Write-Host "Setting PackageReference versions to $Version" -ForegroundColor Cyan

Clear-NuGetPackageCache -version $Version

Write-Host "`nUpdating template project package references..." -ForegroundColor Yellow
foreach ($template in $templateProjects)
{
    if (Test-Path $template.Path)
    {
        Write-Host "Processing $( Split-Path $template.Path -Leaf )..." -ForegroundColor Gray
        foreach ($packageId in $template.PackageReferences)
        {
            [void](Update-PackageReference -projectPath $template.Path -packageId $packageId -version $Version)
        }
    }
    else
    {
        Write-Warning "Template project not found: $( $template.Path )"
    }
}

Write-Host "`nUpdating example project package references..." -ForegroundColor Yellow
foreach ($example in $exampleProjects)
{
    if (Test-Path $example.Path)
    {
        Write-Host "Processing $( Split-Path $example.Path -Leaf )..." -ForegroundColor Gray
        foreach ($packageId in $example.PackageReferences)
        {
            [void](Update-PackageReference -projectPath $example.Path -packageId $packageId -version $Version)
        }
    }
    else
    {
        Write-Warning "Example project not found: $( $example.Path )"
    }
}

Write-Host "`n[+] Package references updated to $Version" -ForegroundColor Green
Write-Host "Commit + push. Examples/templates are not part of the SDK .sln and build independently after restore." -ForegroundColor Gray
